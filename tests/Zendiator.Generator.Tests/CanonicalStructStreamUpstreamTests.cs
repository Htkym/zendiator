using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData("repeat", false)]
    [InlineData("repeat", true)]
    [InlineData("early", false)]
    [InlineData("early", true)]
    [InlineData("open-fault", false)]
    [InlineData("open-fault", true)]
    [InlineData("move-fault", false)]
    [InlineData("move-fault", true)]
    [InlineData("cleanup-fault", false)]
    [InlineData("cleanup-fault", true)]
    public async Task Struct_stream_port_copies_keep_open_cleanup_reopen_and_root_lifetimes(string mode, bool standard)
    {
        var source = NativeStreamFixture("""
            c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));
            c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Inspect>());
            """, """
            public sealed record Items(string Mode):IStreamRequest<int>;
            public sealed class Probe{
                public int Opens,Disposed,Generic,PendingDenied,ActiveDenied,FaultDenied,Defaults;
                public readonly TaskCompletionSource OpenStarted=new(),OpenGate=new(),CleanupStarted=new(),CleanupGate=new();
                public Func<Task<IAsyncEnumerator<int>>>? Retained;
            }
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>{
                public async ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){
                    var id=++p.Opens;
                    if(r.Mode=="repeat"&&id==1){p.OpenStarted.SetResult();await p.OpenGate.Task.ConfigureAwait(false);}
                    if(r.Mode=="open-fault")throw new ArithmeticException("open");
                    return new Leaf(p,r,id,t);
                }
            }
            public sealed class Leaf(Probe p,Items r,int id,CancellationToken token):IAsyncEnumerator<int>{
                private int index=-1;public int Current=>index;
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();if(r.Mode=="move-fault")throw new ArithmeticException("move");return new(++index<2);}
                public async ValueTask DisposeAsync(){
                    p.Disposed++;
                    if(r.Mode=="repeat"&&id==1){p.CleanupStarted.SetResult();await p.CleanupGate.Task.ConfigureAwait(false);}
                    if(r.Mode=="cleanup-fault")throw new ApplicationException("cleanup");
                }
            }
            public sealed class Inspect(Probe p):IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>throw new Exception("legacy path selected");
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{
                    p.Generic++;var copy=next;
                    Check.That(typeof(TNext).IsValueType,"actual port is a value type");
                    var fields=typeof(TNext).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
                    Check.That(fields.Length==1&&!fields[0].FieldType.IsValueType,"port contains only one owner reference");
                    try{default(TNext).OpenAsync(r,t);throw new Exception("default port opened");}catch(InvalidOperationException){p.Defaults++;}
                    p.Retained=()=>copy.OpenAsync(r,t).AsTask();
                    return new(new HeldCursor<TNext>(p,r,next,t));
                }
            }
            public sealed class HeldCursor<TNext>(Probe p,Items r,TNext next,CancellationToken token):IAsyncEnumerator<int>where TNext:struct,IStreamUpstream<Items,int>{
                private readonly TNext port=next;private IAsyncEnumerator<int>? active;private int round;
                public int Current=>active!.Current;
                public async ValueTask<bool> MoveNextAsync(){
                    try{
                        if(active is null){
                            var copy=port;var pending=port.OpenAsync(r,token);
                            if(!pending.IsCompleted){try{_ = copy.OpenAsync(r,token);throw new Exception("copy overlapped pending open");}catch(InvalidOperationException){p.PendingDenied++;}}
                            active=await pending.ConfigureAwait(false);
                            try{_ = copy.OpenAsync(r,token);throw new Exception("copy overlapped active cursor");}catch(InvalidOperationException){p.ActiveDenied++;}
                        }
                        if(await active.MoveNextAsync().ConfigureAwait(false))return true;
                        if(r.Mode=="repeat"&&round++==0){
                            await active.DisposeAsync().ConfigureAwait(false);active=await port.OpenAsync(r,token).ConfigureAwait(false);
                            return await active.MoveNextAsync().ConfigureAwait(false);
                        }
                        return false;
                    }catch{
                        try{await port.OpenAsync(r,token).ConfigureAwait(false);throw new Exception("faulted lease reopened");}catch(InvalidOperationException){p.FaultDenied++;}
                        throw;
                    }
                }
                public ValueTask DisposeAsync()=>active is null?default:active.DisposeAsync();
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var r=new Items("__MODE__");await using var root=m.StreamAsync(r).GetAsyncEnumerator();
            if(r.Mode=="repeat"){
                var first=root.MoveNextAsync().AsTask();await p.OpenStarted.Task;
                Check.That(!first.IsCompleted&&p.PendingDenied==1,"copied port shares pending opening gate");p.OpenGate.SetResult();
                Check.That(await first&&root.Current==0,"first item");Check.That(await root.MoveNextAsync()&&root.Current==1,"second item");
                var reopening=root.MoveNextAsync().AsTask();await p.CleanupStarted.Task;
                try{await p.Retained!();throw new Exception("copy reopened pending cleanup");}catch(InvalidOperationException){}
                p.CleanupGate.SetResult();Check.That(await reopening&&root.Current==0,"normal EOF and cleanup permit reopen");
                Check.That(await root.MoveNextAsync()&&root.Current==1&&!await root.MoveNextAsync(),"second complete cursor");
                Check.That(p.Opens==2&&p.Disposed==2&&p.ActiveDenied==1,"independent children close once");
            }else if(r.Mode=="early"){
                Check.That(await root.MoveNextAsync(),"early first item");await root.DisposeAsync();
                Check.That(p.Opens==1&&p.Disposed==1,"early disposal closes active child once");
            }else{
                Exception? failure=null;try{while(await root.MoveNextAsync()){} }catch(Exception e){failure=e;}
                Check.That(failure?.GetType()==(r.Mode=="cleanup-fault"?typeof(ApplicationException):typeof(ArithmeticException)),"original operation or cleanup failure escapes");
                Check.That(p.Opens==1&&p.FaultDenied==1,"failed child cannot reopen while outer owner is still active");
            }
            try{await p.Retained!();throw new Exception("retained port reopened closed root");}catch(ObjectDisposedException){}
            Check.That(p.Generic==1&&p.Defaults==1,"generic retained port used without legacy dispatch");
            """.Replace("__MODE__", mode));
        if (standard)
            source = source.Replace("IStructStreamMiddleware", "IStreamMiddleware", StringComparison.Ordinal);
        await RunNativeStreamFixture("struct-port-" + mode + "-" + standard, source);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(5, false, false)]
    [InlineData(5, true, false)]
    [InlineData(5, false, true)]
    public async Task Struct_stream_relay_keeps_typed_leaf_identity_tokens_and_both_public_entries(int count, bool differentToken, bool mixed)
    {
        var stages = string.Join(",", Enumerable.Range(1, count).Select(i => mixed && i == 3 ? "Stage.Wrapper<LegacyRelay>()" : "Stage.Wrapper<Relay<Tag" + i + ">>()"));
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream" + (count == 0 ? "" : "," + stages) + ");",
            """
            public sealed record Items(bool Different):IStreamRequest<int>;
            public sealed class Tag1;public sealed class Tag2;public sealed class Tag3;public sealed class Tag4;public sealed class Tag5;
            public sealed class Probe{public int Generic,Legacy,Opened,Disposed,ServicesDisposed;public readonly List<IAsyncEnumerator<int>> Aliases=new();public CancellationToken Other;}
            [ZendiatorHandler]internal sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>,IDisposable{
                public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Leaf(p,t));}public void Dispose()=>p.ServicesDisposed++;
            }
            internal struct Leaf:IAsyncEnumerator<int>{
                private readonly Probe p;private readonly CancellationToken token;private int index;
                public Leaf(Probe p,CancellationToken token){this.p=p;this.token=token;index=-1;}
                public int Current=>index;public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();return new(++index<2);}
                public ValueTask DisposeAsync(){p.Disposed++;return default;}
            }
            public sealed class Relay<TTag>(Probe p):IStructStreamMiddleware<Items,int>,IDisposable{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>throw new Exception("legacy path");
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{
                    p.Generic++;var cursor=await next.OpenAsync(r,r.Different&&typeof(TTag)==typeof(Tag1)?p.Other:t).ConfigureAwait(false);p.Aliases.Add(cursor);return cursor;
                }
                public void Dispose()=>p.ServicesDisposed++;
            }
            public sealed class LegacyRelay(Probe p):IStreamMiddleware<Items,int>,IDisposable{
                public async ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;var cursor=await next.OpenAsync(r,t).ConfigureAwait(false);p.Aliases.Add(cursor);return cursor;}
                public void Dispose()=>p.ServicesDisposed++;
            }
            public readonly struct Sink(List<int> values):IStreamSink<int>{public ValueTask<bool> OnNextAsync(int value,CancellationToken t){values.Add(value);return new(true);}}
            """, """
            using var other=new CancellationTokenSource();var p=new Probe{Other=other.Token};var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            var stageCount=__COUNT__;var replacement=new Relay<Tag1>(p);if(stageCount>0)services.AddSingleton(replacement);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            Check.That(stageCount==0||ReferenceEquals(scope.ServiceProvider.GetRequiredService<Relay<Tag1>>(),replacement),"DI override supplies the borrowed receiver");
            var r=new Items(__DIFFERENT__);var feed=m.StreamAsync(r);var plan=m.StreamSource(r);Check.That(p.Opened==0,"both facade entries stay lazy");
            await using(var cursor=feed.GetAsyncEnumerator()){
                Check.That(await cursor.MoveNextAsync()&&cursor.Current==0,"typed leaf first Current");
                if(stageCount>0&&!r.Different){foreach(var alias in p.Aliases)Check.Invalid(()=>{var value=alias.Current;});}
                if(r.Different){foreach(var alias in p.Aliases)Check.That(alias.Current==0,"different token keeps independent cursor rights");}
                Check.That(await cursor.MoveNextAsync()&&cursor.Current==1&&!await cursor.MoveNextAsync(),"typed leaf complete sequence");
            }
            var values=new List<int>();await plan.ConsumeAsync(new Sink(values));
            Check.That(values.SequenceEqual(new[]{0,1})&&p.Generic==2*(__COUNT__-__MIXED__)&&p.Legacy==2*__MIXED__&&p.Opened==2&&p.Disposed==2,"typed terminal and public enumerable preserve independent identities");
            Check.That(p.ServicesDisposed==0,"services stay borrowed until native scope/provider disposal");await scope.DisposeAsync();await provider.DisposeAsync();Check.That(p.ServicesDisposed==stageCount+1-(stageCount>0?1:0),"native DI disposes owned registrations but not the externally supplied singleton");if(stageCount>0)replacement.Dispose();Check.That(p.ServicesDisposed==stageCount+1,"external singleton retains its caller-owned disposal");
            """.Replace("__MIXED__", mixed ? "1" : "0").Replace("__COUNT__", count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("__DIFFERENT__", differentToken ? "true" : "false"));
        await RunNativeStreamFixture("struct-relay-" + count + "-" + differentToken + "-" + mixed, source);
    }

    [Theory]
    [InlineData("Explicit")]
    [InlineData("Inherited")]
    [InlineData("Nonsealed")]
    [InlineData("Virtual")]
    [InlineData("Overloaded")]
    public async Task Struct_stream_unsupported_direct_receivers_keep_legacy_dispatch(string receiver)
    {
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<" + receiver + ">());",
            """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{public int Legacy,Generic,Overrides;}
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
            public sealed class Leaf:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Overloaded(Probe p):IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;return next.OpenAsync(r,t);}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;throw new Exception("ambiguous direct generic selected");}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,object next,CancellationToken t)where TNext:struct=>throw new Exception("unrelated overload selected");
            }
            public sealed class Explicit(Probe p):IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;return next.OpenAsync(r,t);}
                ValueTask<IAsyncEnumerator<int>> IStructStreamMiddleware<Items,int>.OpenAsync<TNext>(Items r,TNext next,CancellationToken t){p.Generic++;throw new Exception("explicit generic selected");}
            }
            public class Parent(Probe p):IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;return next.OpenAsync(r,t);}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;throw new Exception("inherited generic selected");}
            }
            public sealed class Inherited(Probe p):Parent(p);
            public class Nonsealed(Probe p):IStructStreamMiddleware<Items,int>{
                public virtual ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;return next.OpenAsync(r,t);}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;throw new Exception("nonsealed generic selected");}
            }
            public sealed class Derived:Nonsealed{private readonly Probe probe;public Derived(Probe p):base(p){probe=p;}public override ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){probe.Overrides++;return base.OpenAsync(r,next,t);}}
            public class VirtualParent(Probe p):IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;return next.OpenAsync(r,t);}
                public virtual ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;throw new Exception("virtual generic selected");}
            }
            public sealed class Virtual(Probe p):VirtualParent(p){public override ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)=>throw new Exception("override generic selected");}
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);var receiverName="__RECEIVER__";if(receiverName=="Nonsealed")services.AddSingleton<Nonsealed>(new Derived(p));await using var provider=services.BuildServiceProvider();
            await using var scope=provider.CreateAsyncScope();var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            await foreach(var item in m.StreamAsync(new Items()))throw new Exception("unexpected item");Check.That(p.Legacy==1&&p.Generic==0,"unsupported opt-in keeps existing contract");Check.That(p.Overrides==(receiverName=="Nonsealed"?1:0),"runtime DI subtype keeps legacy virtual dispatch");
            """.Replace("__RECEIVER__", receiver));
        await RunNativeStreamFixture("struct-fallback-" + receiver, source);
    }

    [Theory]
    [InlineData("where TNext:class,IStreamUpstream<Items,int>")]
    [InlineData("where TNext:struct")]
    [InlineData("where TNext:unmanaged,IStreamUpstream<Items,int>")]
    [InlineData("wrong-return")]
    [InlineData("wrong-next")]
    [InlineData("missing-method")]
    public void Struct_stream_invalid_opted_in_signature_or_constraint_has_actionable_stage_diagnostic(string constraint)
    {
        var method = "public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t) " +
            (constraint.StartsWith("where", StringComparison.Ordinal) ? constraint : "where TNext:struct,IStreamUpstream<Items,int>") + " => throw new Exception();";
        if (constraint == "wrong-return")
            method = method.Replace("ValueTask<IAsyncEnumerator<int>>", "ValueTask<Leaf>");
        if (constraint == "wrong-next")
            method = method.Replace("TNext next", "IStreamUpstream<Items,int> next");
        if (constraint == "missing-method")
            method = "";
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Invalid>());",
            """
            public sealed record Items:IStreamRequest<int>;
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
            public sealed class Leaf:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Invalid:IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>next.OpenAsync(r,t);
                __METHOD__
            }
            """.Replace("__METHOD__", method), "await Task.CompletedTask;");
        var result = SyncDriver().RunGenerators(SyncCompilation(source, "StructStreamInvalid")).GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0025" && d.GetMessage().Contains("IStructStreamMiddleware", StringComparison.Ordinal) &&
            d.GetMessage().Contains("struct, IStreamUpstream", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Struct_stream_opt_in_keeps_private_ports_and_typed_leaf_out_of_public_signature(bool standard)
    {
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay>());", """
            public sealed record Items:IStreamRequest<int>;
            [ZendiatorHandler]internal sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
            internal struct Leaf:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Relay:IStructStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>next.OpenAsync(r,t);
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int> =>next.OpenAsync(r,t);
            }
            """, "await Task.CompletedTask;");
        if (standard)
            source = source.Replace("IStructStreamMiddleware", "IStreamMiddleware", StringComparison.Ordinal);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StructStreamShape_" + standard), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var generated = driver.GetRunResult().Results.Single().GeneratedSources.Single(f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("private readonly struct Upstream1", generated, StringComparison.Ordinal);
        Assert.Contains("OpenAsync<Upstream1>", generated, StringComparison.Ordinal);
        Assert.Contains("global::NativeStream.Leaf _source", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("private Upstream1? _upstream", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IStructStreamMiddleware<", generated, StringComparison.Ordinal);
        var facade = output.GetTypeByMetadataName("NativeStream.Generated.Reads.IZendiator")!;
        Assert.All(facade.GetMembers().OfType<IMethodSymbol>(), method =>
            Assert.DoesNotContain("Upstream", method.ReturnType.ToDisplayString(), StringComparison.Ordinal));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var instructions = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)).SelectMany(StructStreamInstructions).ToArray();
        var calls = instructions.Where(i => i.Target is MethodInfo target && target.Name == "OpenAsync" && target.IsGenericMethod &&
            target.GetGenericArguments().Any(a => a.Name == "Upstream1")).ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, call =>
        {
            var target = (MethodInfo)call.Target!;
            Assert.Equal("NativeStream.Relay", target.DeclaringType!.FullName);
            Assert.True(target.DeclaringType.IsSealed);
            Assert.False(target.DeclaringType.IsInterface);
            Assert.True(!target.IsVirtual || target.IsFinal);
        });
        Assert.DoesNotContain(instructions, i => i.Code == OpCodes.Box && i.Target is Type type && type.Name == "Upstream1");
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "struct-stream-il");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "caller-il.json"), System.Text.Json.JsonSerializer.Serialize(calls.Select(call => new
            {
                Caller = call.Caller.DeclaringType!.FullName + "." + call.Caller.Name,
                Opcode = call.Code.Name,
                Target = call.Target!.ToString(),
                DeclaringType = call.Target.DeclaringType!.FullName,
                ConcreteSealed = call.Target.DeclaringType.IsSealed,
                Final = ((MethodInfo)call.Target).IsFinal,
                PortBoxInstructions = instructions.Count(i => i.Code == OpCodes.Box && i.Target is Type type && type.Name == "Upstream1")
            }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "generated.cs"), generated);
        }
    }

    private static IEnumerable<(MethodBase Caller, OpCode Code, MemberInfo? Target)> StructStreamInstructions(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null)
            yield break;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
        var typeArguments = method.DeclaringType!.GetGenericArguments();
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
        for (var offset = 0; offset < bytes.Length;)
        {
            ushort key = bytes[offset++];
            if (key == 0xfe)
                key = (ushort)(0xfe00 | bytes[offset++]);
            var code = opcodes[key];
            MemberInfo? target = null;
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(bytes, offset) * 4,
                _ => 4
            };
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineField)
                target = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset), typeArguments, methodArguments);
            yield return (method, code, target);
            offset += size;
        }
    }


    [Fact]
    public async Task Struct_stream_mutable_value_receiver_preserves_one_borrowed_box_and_legacy_dispatch()
    {
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Mutable>());", """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{public int Legacy,Generic,DisposedCalls;public readonly List<int> Calls=new();}
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
            public sealed class Leaf:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public struct Mutable:IStructStreamMiddleware<Items,int>,IDisposable{
                private readonly Probe p;private int calls;public Mutable(Probe p){this.p=p;calls=0;}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t){p.Legacy++;p.Calls.Add(++calls);return next.OpenAsync(r,t);}
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;throw new Exception("value receiver direct call selected");}
                public void Dispose()=>p.DisposedCalls=calls;
            }
            """, """
            var p=new Probe();object box=new Mutable(p);var services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(typeof(Mutable),box);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            Check.That(ReferenceEquals(scope.ServiceProvider.GetRequiredService(typeof(Mutable)),box),"native DI preserves the same value receiver box");
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            for(var run=0;run<2;run++)await foreach(var item in m.StreamAsync(new Items()))throw new Exception("unexpected item");
            Check.That(p.Legacy==2&&p.Generic==0&&p.Calls.SequenceEqual(new[]{1,2}),"legacy interface calls mutate the same box rather than unboxed copies");
            await scope.DisposeAsync();await provider.DisposeAsync();Check.That(p.DisposedCalls==0,"caller-supplied instance stays caller-owned");
            ((IDisposable)box).Dispose();Check.That(p.DisposedCalls==2,"retained box contains both mutations");
            """);
        await RunNativeStreamFixture("struct-value-receiver-fallback", source);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("open")]
    [InlineData("cleanup")]
    public async Task Struct_stream_owned_wrapper_session_returns_once_on_failed_factory_open_or_cleanup(string mode)
    {
        var source = NativeStreamFixture("c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Owned,Factory>());", """
            public sealed record Items(string Mode):IStreamRequest<int>;
            public sealed class Probe{public int Created,Returned,Generic,Opened,LeafClosed,FactoryDisposed,HandlerDisposed;}
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>,IDisposable{
                public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){p.Opened++;if(r.Mode=="open")throw new ArithmeticException("open");return new(new Leaf(p));}
                public void Dispose()=>p.HandlerDisposed++;
            }
            public sealed class Leaf(Probe p):IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync(){p.LeafClosed++;throw new ApplicationException("cleanup");}}
            public sealed class Factory(Probe p):IOwnedSessionFactory<Items,Owned>,IDisposable{
                public Owned Create(Items r,CancellationToken t){if(r.Mode=="factory")throw new FormatException("factory");p.Created++;return new Owned(p);}
                public void Dispose()=>p.FactoryDisposed++;
            }
            public sealed class Owned(Probe p):IStructStreamMiddleware<Items,int>,IAsyncDisposable{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>throw new Exception("owned legacy path");
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;return next.OpenAsync(r,t);}
                public ValueTask DisposeAsync(){p.Returned++;return default;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();
            var m=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();var mode="__MODE__";
            var cursor=m.StreamAsync(new Items(mode)).GetAsyncEnumerator();Exception? error=null;try{await cursor.MoveNextAsync();}catch(Exception e){error=e;}
            Check.That(error?.GetType()==(mode=="factory"?typeof(FormatException):mode=="open"?typeof(ArithmeticException):typeof(ApplicationException)),"acquisition/open/cleanup error priority is preserved");
            await cursor.DisposeAsync();await cursor.DisposeAsync();var acquired=mode=="factory"?0:1;
            Check.That(p.Created==acquired&&p.Returned==acquired&&p.Generic==acquired&&p.Opened==acquired,"each acquired owned generic wrapper is returned exactly once");
            Check.That(p.LeafClosed==(mode=="cleanup"?1:0)&&p.FactoryDisposed==0&&p.HandlerDisposed==0,"only acquired source cleanup occurs and DI dependencies stay borrowed");
            await scope.DisposeAsync();await provider.DisposeAsync();Check.That(p.FactoryDisposed==1&&p.HandlerDisposed==acquired,"native DI alone disposes the borrowed factory and activated handler");
            """.Replace("__MODE__", mode));
        await RunNativeStreamFixture("struct-owned-failure-" + mode, source);
    }

    // Reuse existing complex source/context fixtures with only the optional contract added.
    private static string WithStructStreamMiddleware(string source) =>
        new StructStreamFixtureRewriter().Visit(CSharpSyntaxTree.ParseText(source).GetRoot())!.NormalizeWhitespace().ToFullString();

    private sealed class StructStreamFixtureRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var result = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            if (!result.Modifiers.Any(SyntaxKind.SealedKeyword) || result.BaseList == null)
                return result;
            var bases = result.BaseList.Types.Where(b => b.Type is GenericNameSyntax generic && generic.Identifier.ValueText == "IStreamMiddleware").ToArray();
            var methods = result.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "OpenAsync" &&
                m.TypeParameterList == null && m.ExplicitInterfaceSpecifier == null && m.ParameterList.Parameters.Count == 3).ToArray();
            if (bases.Length != 1 || methods.Length != 1)
                return result;
            var method = methods[0];
            var parameter = method.ParameterList.Parameters[1];
            var constraints = ((MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                "void Added<__StructNext>() where __StructNext : struct, " + parameter.Type + " {}")!).ConstraintClauses;
            var typed = method.WithTypeParameterList(SyntaxFactory.TypeParameterList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.TypeParameter("__StructNext"))))
                .WithParameterList(method.ParameterList.ReplaceNode(parameter, parameter.WithType(SyntaxFactory.IdentifierName("__StructNext"))))
                .WithConstraintClauses(constraints);
            var genericBase = (GenericNameSyntax)bases[0].Type;
            return result.WithBaseList(result.BaseList.ReplaceNode(bases[0], bases[0].WithType(
                    genericBase.WithIdentifier(SyntaxFactory.Identifier("IStructStreamMiddleware")))))
                .AddMembers(typed);
        }
    }
}
