using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed record PublicValueRequestObservation(string Phase, int Marker, long Pad, object? Shared, int Version);

public sealed class PublicValueRequestProbe
{
    public readonly List<PublicValueRequestObservation> Seen = [];
    public readonly List<string> Resolved = [], Ended = [];
    public int Starts, SourceDisposals, Sends;
    public void Record(string phase, int marker, long pad, object? shared, int version) => Seen.Add(new(phase, marker, pad, shared, version));
    public static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

public sealed partial class GeneratorTests
{
    private static string PublicValueRequestSource(bool unsupported) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using System.Text.Json;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        using Zendiator.Generator.Tests;
        namespace ValuePublic {
        public sealed class SharedState { public int Version; }
        public struct Items : IStreamRequest<int> {
            public int Marker;public long Pad;public SharedState? Shared;
            public Items(SharedState shared){Marker=7;Pad=11;Shared=shared;}
        }
        public sealed record Query(int Value):ISyncRequest<int>;
        [ZendiatorHandler]public sealed class H: IStreamRequestHandler<Items,int>,IDisposable {
            private readonly PublicValueRequestProbe p;
            public H(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("H");}
            public IAsyncEnumerable<int> HandleAsync(Items r,CancellationToken t){
                p.Starts++;p.Record("H",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);
                r.Marker=101;r.Pad=1001;r.Shared!.Version++;return new Source(p);
            }
            public void Dispose()=>p.Ended.Add("H");
        }
        public sealed class F1:IStreamItemFilter<Items,int>,IDisposable {
            private readonly PublicValueRequestProbe p;public F1(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("F1");}
            public bool Accept(Items r,int item,CancellationToken t){p.Record("F1",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=102;r.Pad=1002;return item>0;}
            public void Dispose()=>p.Ended.Add("F1");
        }
        public sealed class M:IStreamItemMap<Items,int>,IDisposable {
            private readonly PublicValueRequestProbe p;public M(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("M");}
            public int Map(Items r,int item,CancellationToken t){p.Record("M",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=103;r.Pad=1003;return item*2;}
            public void Dispose()=>p.Ended.Add("M");
        }
        public sealed class F2:IStreamItemFilter<Items,int>,IDisposable {
            private readonly PublicValueRequestProbe p;public F2(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("F2");}
            public bool Accept(Items r,int item,CancellationToken t){p.Record("F2",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=104;r.Pad=1004;return item<6;}
            public void Dispose()=>p.Ended.Add("F2");
        }
        public sealed class Source(PublicValueRequestProbe p):IAsyncEnumerable<int> {
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken t=default)=>new Cursor(p);
            private sealed class Cursor(PublicValueRequestProbe p):IAsyncEnumerator<int> {
                private int value;public int Current=>value;
                public ValueTask<bool> MoveNextAsync()=>new(++value<=3);
                public ValueTask DisposeAsync(){p.SourceDisposals++;return default;}
            }
        }
        [ZendiatorHandler]public sealed class QH:ISyncRequestHandler<Query,int>,IDisposable {
            private readonly PublicValueRequestProbe p;public QH(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("Q");}
            public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}
            public void Dispose()=>p.Ended.Add("Q");
        }
        public struct GenericItems<T>:IStreamRequest<int> {}
        [ZendiatorHandler]public sealed class GH<T>:IStreamRequestHandler<GenericItems<T>,int>{
            public IAsyncEnumerable<int> HandleAsync(GenericItems<T> r,CancellationToken t)=>throw new NotSupportedException();
        }
        public sealed class GF1<T>:IStreamItemFilter<GenericItems<T>,int>{public bool Accept(GenericItems<T> r,int item,CancellationToken t)=>true;}
        public sealed class GM<T>:IStreamItemMap<GenericItems<T>,int>{public int Map(GenericItems<T> r,int item,CancellationToken t)=>item;}
        public sealed class GF2<T>:IStreamItemFilter<GenericItems<T>,int>{public bool Accept(GenericItems<T> r,int item,CancellationToken t)=>true;}
        [ZendiatorComposition("value-single")]public sealed class Single;
        [ZendiatorComposition("value-mixed")]public sealed class Mixed;
        public static class Bridge {
            public static IServiceCollection RegisterSingle(IServiceCollection s)=>s.AddZendiator<Single>(c=>{
                c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
            });
            public static IServiceCollection RegisterMixed(IServiceCollection s)=>s.AddZendiator<Mixed>(c=>{
                c.Handlers<__REQUEST__>(DispatchFamily.Stream,typeof(__HANDLER__));
                c.Handlers<Query>(DispatchFamily.SendSync,typeof(QH));
                c.Pipeline<Query>(DispatchFamily.SendSync);
                c.Pipeline<__REQUEST__>(DispatchFamily.Stream,Stage.Hook<__FIRST__>(),Stage.Hook<__MAP__>(),Stage.Hook<__LAST__>());
            });
            __CONSUMER__
        }
        }
        """.Replace("__REQUEST__", unsupported ? "GenericItems<int>" : "Items")
        .Replace("__HANDLER__", unsupported ? "GH<int>" : "H")
        .Replace("__FIRST__", unsupported ? "GF1<int>" : "F1")
        .Replace("__MAP__", unsupported ? "GM<int>" : "M")
        .Replace("__LAST__", unsupported ? "GF2<int>" : "F2")
        .Replace("__CONSUMER__", unsupported ? "" : PublicValueRequestConsumer);

    private const string PublicValueRequestConsumer = """
        private static void Check(bool value,string message)=>PublicValueRequestProbe.Check(value,message);
        private static object Observations(PublicValueRequestProbe p,SharedState shared)=>p.Seen.Select(s=>new{
            s.Phase,s.Marker,s.Pad,s.Version,shared=ReferenceEquals(s.Shared,shared)}).ToArray();
        private static void CheckCopy(PublicValueRequestProbe p,SharedState shared){
            Check(p.Seen.Count==10,"callback count");Check(p.Seen[0].Phase=="H"&&p.Seen[0].Version==0,"handler snapshot");
            Check(p.Seen.All(s=>s.Marker==7&&s.Pad==11&&ReferenceEquals(s.Shared,shared)),"typed snapshot and shared reference");
            Check(p.Seen.Skip(1).All(s=>s.Version==1)&&shared.Version==1,"shared member mutation");
            Check(p.Seen.Select(s=>s.Phase).SequenceEqual(new[]{"H","F1","M","F2","F1","M","F2","F1","M","F2"}),"callback order");
        }
        private static void Unique(IServiceCollection s)=>Check(s.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
        public static async Task<string> Run(){
            var singleProbe=new PublicValueRequestProbe();var singleShared=new SharedState();
            IServiceCollection singleServices=new ServiceCollection();singleServices.AddSingleton(singleProbe);RegisterSingle(singleServices);Unique(singleServices);
            using var singleProvider=singleServices.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            var scope=singleProvider.CreateScope();
            var single=scope.ServiceProvider.GetRequiredService<global::ValuePublic.Generated.Single.IZendiator>();
            Check(ReferenceEquals(single,scope.ServiceProvider.GetRequiredService<global::ValuePublic.Generated.Single.Zendiator>()),"single mediator identity");
            var request=new Items(singleShared);var stream=single.StreamAsync(request);
            request.Marker=999;request.Pad=777;
            var cursor=stream.GetAsyncEnumerator();
            Check(singleProbe.Starts==0&&singleProbe.Resolved.Count==0,"single dispatch/cursor lazy");
            var singleValues=new List<int>();
            try{while(await cursor.MoveNextAsync())singleValues.Add(cursor.Current);}finally{await cursor.DisposeAsync();}
            Check(singleValues.SequenceEqual(new[]{2,4})&&singleProbe.Starts==1&&singleProbe.SourceDisposals==1,"single result/lifecycle");
            Check(singleProbe.Ended.Count==0,"single borrowed until scope");CheckCopy(singleProbe,singleShared);
            Check(request.Marker==999&&request.Pad==777,"caller local unchanged by callbacks");
            scope.Dispose();Check(singleProbe.Ended.SequenceEqual(new[]{"F2","M","F1","H"}),"single scope ownership");
            var deadScope=singleProvider.CreateScope();
            var dead=deadScope.ServiceProvider.GetRequiredService<global::ValuePublic.Generated.Single.IZendiator>();
            var defaultStream=dead.StreamAsync(default(Items));var defaultCursor=defaultStream.GetAsyncEnumerator();
            Check(singleProbe.Resolved.Count==4&&singleProbe.Starts==1,"default dispatch/cursor lazy");
            deadScope.Dispose();bool defaultFailedAtDI=false;
            try{await defaultCursor.MoveNextAsync();throw new InvalidOperationException("expected disposed scope");}
            catch(ObjectDisposedException){defaultFailedAtDI=true;}
            finally{await defaultCursor.DisposeAsync();}
            Check(singleProbe.Resolved.Count==4&&singleProbe.Starts==1&&singleProbe.SourceDisposals==1,"default no callback or source");
            var mixedProbe=new PublicValueRequestProbe();var mixedShared=new SharedState();
            IServiceCollection mixedServices=new ServiceCollection();mixedServices.AddSingleton(mixedProbe);mixedServices.AddSingleton<QH>();RegisterMixed(mixedServices);Unique(mixedServices);
            var mixedProvider=mixedServices.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            var mixedScope=mixedProvider.CreateScope();
            var mixed=mixedScope.ServiceProvider.GetRequiredService<global::ValuePublic.Generated.Mixed.IZendiator>();
            var mixedRequest=new Items(mixedShared);var mixedStream=mixed.StreamAsync(mixedRequest);
            mixedRequest.Marker=999;mixedRequest.Pad=777;var mixedCursor=mixedStream.GetAsyncEnumerator();
            Check(mixedProbe.Starts==0&&mixedProbe.Resolved.Count==0,"mixed dispatch/cursor lazy");
            var sent=mixed.Send(new Query(10));
            Check(sent==11&&mixedProbe.Sends==1&&mixedProbe.Starts==0&&mixedProbe.Resolved.SequenceEqual(new[]{"Q"}),"Send isolated");
            var mixedValues=new List<int>();
            try{while(await mixedCursor.MoveNextAsync())mixedValues.Add(mixedCursor.Current);}finally{await mixedCursor.DisposeAsync();}
            Check(mixedValues.SequenceEqual(new[]{2,4})&&mixedProbe.Starts==1&&mixedProbe.SourceDisposals==1,"mixed result/lifecycle");
            CheckCopy(mixedProbe,mixedShared);Check(mixedProbe.Ended.Count==0,"mixed borrowed until scope");
            mixedScope.Dispose();var mixedScopeEnds=mixedProbe.Ended.ToArray();
            Check(mixedScopeEnds.SequenceEqual(new[]{"F2","M","F1","H"}),"mixed scope ownership");
            mixedProvider.Dispose();Check(mixedProbe.Ended.SequenceEqual(new[]{"F2","M","F1","H","Q"}),"provider owns singleton");
            return JsonSerializer.Serialize(new{
                singleValues,mixedValues,sent,defaultFailedAtDI,
                singleStarts=singleProbe.Starts,singleDisposals=singleProbe.SourceDisposals,
                mixedStarts=mixedProbe.Starts,mixedDisposals=mixedProbe.SourceDisposals,mixedProbe.Sends,
                singleSeen=Observations(singleProbe,singleShared),mixedSeen=Observations(mixedProbe,mixedShared),
                singleEnds=singleProbe.Ended,mixedScopeEnds,mixedEnds=mixedProbe.Ended
            });
        }
        """;

    private sealed record PublicValueIlInventory(string Scope, string[] Owners, string[] Boxes, string[] TypedFields, string[] TypedCalls);
    private static PublicValueIlInventory PublicValueRequestIl(Assembly assembly)
    {
        var request = assembly.GetType("ValuePublic.Items")!;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var boxes = new List<string>(); var fields = new List<string>(); var calls = new List<string>(); var owners = new HashSet<string>();
        foreach (var type in assembly.GetTypes().Where(t =>
        {
            var owner = t; while (owner.DeclaringType != null) owner = owner.DeclaringType;
            var match = owner.Name.StartsWith("__ZendiatorStreamPrototype_", StringComparison.Ordinal) ||
                owner.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal) ||
                owner.FullName is "ValuePublic.Generated.Single.Zendiator" or "ValuePublic.Generated.Mixed.Zendiator";
            if (match) owners.Add(owner.FullName!);
            return match;
        }))
        {
            fields.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType == request).Select(f => type.FullName + "::" + f.Name + ":" + f.FieldType));
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
            {
                var body = method.GetMethodBody()?.GetILAsByteArray(); if (body == null) continue;
                var context = type.GetGenericArguments(); var parameters = method is MethodInfo { IsGenericMethod: true } m ? m.GetGenericArguments() : Type.EmptyTypes;
                for (var i = 0; i < body.Length;)
                {
                    var at = i; ushort code = body[i++]; if (code == 0xFE) code = (ushort)(0xFE00 | body[i++]); var op = codes[code];
                    var size = op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, i),
                        _ => 4
                    };
                    if (op == OpCodes.Box && method.Module.ResolveType(BitConverter.ToInt32(body, i), context, parameters) == request)
                        boxes.Add(type.FullName + "::" + method.Name + ":" + at);
                    if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                    {
                        var called = method.Module.ResolveMethod(BitConverter.ToInt32(body, i), context, parameters)!;
                        if (called.GetParameters().Any(p => p.ParameterType == request)) calls.Add(type.FullName + "::" + method.Name + ":" + at + "->" + called);
                    }
                    i += size; Assert.True(i <= body.Length);
                }
            }
        }
        return new("public generated mediators, existing Stream backends and compiler descendants; request-specific box and typed storage/calls; no JIT/performance claim",
            owners.OrderBy(x => x, StringComparer.Ordinal).ToArray(), boxes.ToArray(), fields.Distinct().ToArray(), calls.ToArray());
    }

    [Fact]
    public async Task Public_zero_stage_struct_Stream_connects_canonical_with_typed_copy_and_rejects_generic_mixture()
    {
        static string ZeroStages(string source) => source
            .Replace("DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>()", "DispatchFamily.Stream")
            .Replace("DispatchFamily.Stream,Stage.Hook<GF1<int>>(),Stage.Hook<GM<int>>(),Stage.Hook<GF2<int>>()", "DispatchFamily.Stream")
            .Replace("p.Seen.Count==10", "p.Seen.Count==1")
            .Replace("new[]{\"H\",\"F1\",\"M\",\"F2\",\"F1\",\"M\",\"F2\",\"F1\",\"M\",\"F2\"}", "new[]{\"H\"}")
            .Replace("new[]{2,4}", "new[]{1,2,3}")
            .Replace("new[]{\"F2\",\"M\",\"F1\",\"H\"}", "new[]{\"H\"}")
            .Replace("new[]{\"F2\",\"M\",\"F1\",\"H\",\"Q\"}", "new[]{\"H\",\"Q\"}")
            .Replace("singleProbe.Resolved.Count==4", "singleProbe.Resolved.Count==1");
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        void Save(string name, string input, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics, byte[]? dll = null)
        {
            if (string.IsNullOrEmpty(evidence)) return;
            var folder = Path.Combine(evidence, "stream-zero-value-public", name);Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), input);
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (dll != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), dll);
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = driver.GetRunResult().Results.Single().GeneratedSources.Select(f => f.HintName)
            }));
        }
        var source = ZeroStages(PublicValueRequestSource(false));
        var assemblyName = "PublicZeroValue_" + Guid.NewGuid().ToString("N");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, assemblyName), out var output, out var diagnostics);
        using var bytes = new MemoryStream();var emitted = output.Emit(bytes);
        Save("accepted", source, driver, output, diagnostics, bytes.ToArray());
        Assert.Empty(diagnostics);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(2, files.Count(f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)));
        Assert.DoesNotContain(files, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var rejected = ZeroStages(PublicValueRequestSource(true));
        var rd = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(rejected, assemblyName), out var ro, out var errors);
        Save("generic-rejected", rejected, rd, ro, errors);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, d => Assert.Equal("ZEN0037", d.Id));
        Assert.Empty(ro.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.NotNull(ro.GetTypeByMetadataName("ValuePublic.Generated.Single.IZendiator"));
        Assert.Null(ro.GetTypeByMetadataName("ValuePublic.Generated.Mixed.IZendiator"));
        Assert.Null(ro.GetTypeByMetadataName("ValuePublic.Generated.Mixed.ZendiatorGeneratedRegistrar"));
        bytes.Position = 0;var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var inventory = PublicValueRequestIl(assembly);
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-zero-value-public", "accepted", "request-il.json"), JsonSerializer.Serialize(inventory));
        Assert.Equal(4, inventory.Owners.Length);Assert.Empty(inventory.Boxes);Assert.NotEmpty(inventory.TypedFields);
        var run = assembly.GetType("ValuePublic.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var runtime = await run().WaitAsync(TimeSpan.FromSeconds(15));
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-zero-value-public", "accepted", "runtime.json"), runtime);
        using var observed = JsonDocument.Parse(runtime);
        Assert.True(observed.RootElement.GetProperty("defaultFailedAtDI").GetBoolean());
        Assert.Equal(new[] { 1, 2, 3 }, observed.RootElement.GetProperty("singleValues").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, observed.RootElement.GetProperty("mixedValues").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task Public_struct_Stream_preserves_typed_copy_lazy_DI_and_mixed_composition_boundary()
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var resume = Environment.GetEnvironmentVariable("ZENDIATOR_PUBLIC_VALUE_RESUME");
        if (!string.IsNullOrEmpty(resume))
        {
            await ResumePublicValueRequest(resume, evidence);
            return;
        }
        void Save(string name, string input, GeneratorDriver driver, Compilation output, IEnumerable<Diagnostic> diagnostics, byte[]? dll = null)
        {
            if (string.IsNullOrEmpty(evidence)) return;
            var folder = Path.Combine(evidence, "stream-value-public", name); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), input);
            var files = driver.GetRunResult().Results.Single().GeneratedSources;
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            if (dll != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), dll);
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        var source = PublicValueRequestSource(false);
        var assemblyName = "PublicValue_" + Guid.NewGuid().ToString("N");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, assemblyName), out var output, out var diagnostics);
        Save("accepted", source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        foreach (var name in new[] { "Single", "Mixed" })
        {
            var mediator = output.GetTypeByMetadataName("ValuePublic.Generated." + name + ".IZendiator");
            Assert.NotNull(mediator); Assert.Single(mediator.GetMembers("StreamAsync"));
            Assert.NotNull(output.GetTypeByMetadataName("ValuePublic.Generated." + name + ".ZendiatorGeneratedRegistrar"));
        }
        Assert.Single(output.GetTypeByMetadataName("ValuePublic.Generated.Mixed.IZendiator")!.GetMembers("Send"));
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(2, files.Count(f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal)));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Save("accepted", source, driver, output, diagnostics, bytes.ToArray());
        var rejected = PublicValueRequestSource(true);
        var rd = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(rejected, assemblyName), out var ro, out var errors);
        Save("unsupported", rejected, rd, ro, errors);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, diagnostic =>
        {
            Assert.Equal("ZEN0037", diagnostic.Id); Assert.NotNull(diagnostic.Location.SourceTree);
            Assert.StartsWith("c.Pipeline<", rejected.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        });
        Assert.Contains(errors, d => rejected.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("c.Pipeline<Query>", StringComparison.Ordinal));
        Assert.Contains(errors, d => rejected.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).StartsWith("c.Pipeline<GenericItems<int>>", StringComparison.Ordinal));
        Assert.Empty(ro.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.NotNull(ro.GetTypeByMetadataName("ValuePublic.Generated.Single.IZendiator"));
        Assert.Null(ro.GetTypeByMetadataName("ValuePublic.Generated.Mixed.IZendiator"));
        Assert.Null(ro.GetTypeByMetadataName("ValuePublic.Generated.Mixed.ZendiatorGeneratedRegistrar"));
        var rejectedFiles = rd.GetRunResult().Results.Single().GeneratedSources;
        Assert.DoesNotContain(rejectedFiles, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) &&
            f.SourceText.ToString().Contains("ValuePublic.Generated.Mixed.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        var normalSingle = Assert.Single(files, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
        Assert.Equal(normalSingle.SourceText.ToString(), Assert.Single(rejectedFiles, f => f.HintName == normalSingle.HintName).SourceText.ToString());
        await ExecutePublicValueRequest(bytes.ToArray(), evidence);
    }

    // A finite local run can resume its saved two inputs without invoking the generator again.
    private static async Task ResumePublicValueRequest(string root, string? evidence)
    {
        using var pins = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "resume-pins.json")));
        foreach (var pin in pins.RootElement.EnumerateArray())
            Assert.Equal(pin.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, pin.GetProperty("path").GetString()!)))));
        var prior = Path.Combine(root, "limited-1", "evidence", "stream-value-public");
        Dictionary<string, string> Sources(string shape) => Directory.GetFiles(Path.Combine(prior, shape), "*.g.cs")
            .ToDictionary(p => Path.GetFileName(p)!, File.ReadAllText);
        var accepted = Sources("accepted"); var rejected = Sources("unsupported");
        foreach (var shape in new[] { "accepted", "unsupported" })
        {
            using var compile = JsonDocument.Parse(File.ReadAllText(Path.Combine(prior, shape, "compile.json")));
            Assert.DoesNotContain(compile.RootElement.GetProperty("compiler").EnumerateArray(), d => d.GetProperty("severity").GetString() is "Error" or "Warning");
            var diagnostics = compile.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            if (shape == "accepted") Assert.Empty(diagnostics);
            else
            {
                Assert.Equal(2, diagnostics.Length); var source = File.ReadAllText(Path.Combine(prior, shape, "input.cs"));
                var anchors = new List<string>();
                foreach (var d in diagnostics)
                {
                    Assert.Equal("ZEN0037", d.GetProperty("Id").GetString());
                    var span = d.GetProperty("span").GetString()![1..^1].Split("..");
                    anchors.Add(source[int.Parse(span[0], System.Globalization.CultureInfo.InvariantCulture)..int.Parse(span[1], System.Globalization.CultureInfo.InvariantCulture)]);
                }
                Assert.Contains(anchors, a => a.StartsWith("c.Pipeline<Query>", StringComparison.Ordinal));
                Assert.Contains(anchors, a => a.StartsWith("c.Pipeline<GenericItems<int>>", StringComparison.Ordinal));
            }
        }
        bool Has(Dictionary<string, string> sources, string ns, string name) => sources.Values.Any(source =>
            CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Any(t =>
                t.Identifier.ValueText == name && string.Join(".", t.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString())) == ns));
        Assert.True(Has(accepted, "ValuePublic.Generated.Single", "IZendiator"));
        Assert.True(Has(accepted, "ValuePublic.Generated.Mixed", "IZendiator"));
        Assert.True(Has(rejected, "ValuePublic.Generated.Single", "IZendiator"));
        Assert.False(Has(rejected, "ValuePublic.Generated.Mixed", "IZendiator"));
        Assert.False(Has(rejected, "ValuePublic.Generated.Mixed", "ZendiatorGeneratedRegistrar"));
        Assert.DoesNotContain(rejected.Values, source => source.Contains("ValuePublic.Generated.Mixed.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        var single = Assert.Single(accepted, f => f.Key.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal));
        Assert.Equal(single.Value, rejected[single.Key]);
        await ExecutePublicValueRequest(File.ReadAllBytes(Path.Combine(prior, "accepted", "generated.dll")), evidence);
    }

    private static async Task ExecutePublicValueRequest(byte[] dll, string? evidence)
    {
        if (!string.IsNullOrEmpty(evidence)) Directory.CreateDirectory(Path.Combine(evidence, "stream-value-public", "accepted"));
        using var bytes = new MemoryStream(dll); var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var inventory = PublicValueRequestIl(assembly);
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-value-public", "accepted", "request-il.json"), JsonSerializer.Serialize(inventory));
        Assert.Equal(4, inventory.Owners.Length); Assert.Empty(inventory.Boxes); Assert.NotEmpty(inventory.TypedFields);
        Assert.Contains(inventory.TypedCalls, c => c.StartsWith("ValuePublic.Generated.Single.Zendiator::StreamAsync", StringComparison.Ordinal));
        Assert.Contains(inventory.TypedCalls, c => c.StartsWith("ValuePublic.Generated.Mixed.Zendiator::StreamAsync", StringComparison.Ordinal));
        var run = assembly.GetType("ValuePublic.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var result = await run().WaitAsync(TimeSpan.FromSeconds(15));
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-value-public", "accepted", "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); var value = observed.RootElement;
        Assert.True(value.GetProperty("defaultFailedAtDI").GetBoolean()); Assert.Equal(11, value.GetProperty("sent").GetInt32());
        foreach (var name in new[] { "singleStarts", "singleDisposals", "mixedStarts", "mixedDisposals", "Sends" }) Assert.Equal(1, value.GetProperty(name).GetInt32());
        Assert.Equal(new[] { 2, 4 }, value.GetProperty("singleValues").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 2, 4 }, value.GetProperty("mixedValues").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public void Public_struct_Stream_gate_accepts_multiple_value_routes_and_rejects_generic_mixture()
    {
        var source = PublicValueRequestSource(false) + "\n" +
            ClosedModule(MixedStreamSendSource(false), "GateClass", "Items", "Probe", "ItemsHandler", "Positive", "DoubleItem", "Small") + "\n" +
            ClosedModule(PublicValueRequestSource(false), "GateValue", "SharedState", "Items", "H", "F1", "M", "F2", "Source") + """
            namespace ValuePublic {
            public sealed record AsyncQuery(int Value):IRequest<int>;
            [ZendiatorHandler]public sealed class AH:IRequestHandler<AsyncQuery,int>{
                public ValueTask<int> HandleAsync(AsyncQuery r,CancellationToken t)=>new(r.Value+1);
            }
            public sealed record ExtraQuery(int Value):ISyncRequest<int>;
            public readonly record struct ValueQuery(int Value):ISyncRequest<int>;
            [ZendiatorHandler]public sealed class EH:ISyncRequestHandler<ExtraQuery,int>{
                public int Handle(ExtraQuery r,CancellationToken t)=>r.Value+1;
            }
            [ZendiatorHandler]public sealed class VH:ISyncRequestHandler<ValueQuery,int>{
                public int Handle(ValueQuery r,CancellationToken t)=>r.Value+1;
            }
            [ZendiatorComposition("value-async")]public sealed class ValueAsync;
            [ZendiatorComposition("value-many")]public sealed class ValueMany;
            [ZendiatorComposition("value-streams")]public sealed class ValueStreams;
            [ZendiatorComposition("two-value-streams")]public sealed class TwoValueStreams;
            [ZendiatorComposition("generic-mixed")]public sealed class GenericMixed;
            [ZendiatorComposition("value-send-only")]public sealed class ValueSendOnly;
            [ZendiatorComposition("class-only")]public sealed class ClassOnly;
            public static class GateBridge {
                public static IServiceCollection Async(IServiceCollection s)=>s.AddZendiator<ValueAsync>(c=>{
                    c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                    c.Handlers<AsyncQuery>(DispatchFamily.SendAsync,typeof(AH));
                    c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
                    c.Pipeline<AsyncQuery>(DispatchFamily.SendAsync);
                });
                public static IServiceCollection Many(IServiceCollection s)=>s.AddZendiator<ValueMany>(c=>{
                    c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                    c.Handlers<Query>(DispatchFamily.SendSync,typeof(QH));
                    c.Handlers<ExtraQuery>(DispatchFamily.SendSync,typeof(EH));
                    c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
                    c.Pipeline<Query>(DispatchFamily.SendSync);
                    c.Pipeline<ExtraQuery>(DispatchFamily.SendSync);
                });
                public static IServiceCollection Streams(IServiceCollection s)=>s.AddZendiator<ValueStreams>(c=>{
                    c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                    c.Handlers<global::Closed.GateClass.Items>(DispatchFamily.Stream,typeof(global::Closed.GateClass.ItemsHandler));
                    c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
                    c.Pipeline<global::Closed.GateClass.Items>(DispatchFamily.Stream,Stage.Hook<global::Closed.GateClass.Positive>(),Stage.Hook<global::Closed.GateClass.DoubleItem>(),Stage.Hook<global::Closed.GateClass.Small>());
                });
                public static IServiceCollection TwoValues(IServiceCollection s)=>s.AddZendiator<TwoValueStreams>(c=>{
                    c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                    c.Handlers<global::Closed.GateValue.Items>(DispatchFamily.Stream,typeof(global::Closed.GateValue.H));
                    c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
                    c.Pipeline<global::Closed.GateValue.Items>(DispatchFamily.Stream,Stage.Hook<global::Closed.GateValue.F1>(),Stage.Hook<global::Closed.GateValue.M>(),Stage.Hook<global::Closed.GateValue.F2>());
                });
                public static IServiceCollection Generic(IServiceCollection s)=>s.AddZendiator<GenericMixed>(c=>{
                    c.Handlers<GenericItems<int>>(DispatchFamily.Stream,typeof(GH<int>));
                    c.Handlers<Query>(DispatchFamily.SendSync,typeof(QH));
                    c.Pipeline<GenericItems<int>>(DispatchFamily.Stream,Stage.Hook<GF1<int>>(),Stage.Hook<GM<int>>(),Stage.Hook<GF2<int>>());
                    c.Pipeline<Query>(DispatchFamily.SendSync);
                });
                public static IServiceCollection ValueSends(IServiceCollection s)=>s.AddZendiator<ValueSendOnly>(c=>{
                    c.Handlers<ValueQuery>(DispatchFamily.SendSync,typeof(VH));
                    c.Handlers<Query>(DispatchFamily.SendSync,typeof(QH));
                    c.Pipeline<ValueQuery>(DispatchFamily.SendSync);
                    c.Pipeline<Query>(DispatchFamily.SendSync);
                });
                public static IServiceCollection Classes(IServiceCollection s)=>s.AddZendiator<ClassOnly>(c=>{
                    c.Handlers<global::Closed.GateClass.Items>(DispatchFamily.Stream,typeof(global::Closed.GateClass.ItemsHandler));
                    c.Handlers<Query>(DispatchFamily.SendSync,typeof(QH));
                    c.Handlers<AsyncQuery>(DispatchFamily.SendAsync,typeof(AH));
                    c.Pipeline<global::Closed.GateClass.Items>(DispatchFamily.Stream,Stage.Hook<global::Closed.GateClass.Positive>(),Stage.Hook<global::Closed.GateClass.DoubleItem>(),Stage.Hook<global::Closed.GateClass.Small>());
                    c.Pipeline<Query>(DispatchFamily.SendSync);
                    c.Pipeline<AsyncQuery>(DispatchFamily.SendAsync);
                });
            }}
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicValueGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence, "stream-value-gate"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.NotNull(d.Location.SourceTree); });
        var genericStart = source.IndexOf("public static IServiceCollection Generic(", StringComparison.Ordinal);
        var genericEnd = source.IndexOf("public static IServiceCollection ValueSends(", genericStart, StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.InRange(d.Location.SourceSpan.Start, genericStart, genericEnd - 1));
        var rejectedAnchors = diagnostics.Select(d => source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)).ToArray();
        Assert.Contains(rejectedAnchors, a => a.StartsWith("c.Pipeline<GenericItems<int>>", StringComparison.Ordinal));
        Assert.Contains(rejectedAnchors, a => a.StartsWith("c.Pipeline<Query>", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        foreach (var name in new[] { "Single", "Mixed", "ClassOnly", "ValueSendOnly", "ValueAsync", "ValueMany", "ValueStreams", "TwoValueStreams" })
        {
            var ns = "ValuePublic.Generated." + name;
            Assert.NotNull(output.GetTypeByMetadataName(ns + ".IZendiator"));
            Assert.NotNull(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
            Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) &&
                f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        }
        var classes = output.GetTypeByMetadataName("ValuePublic.Generated.ClassOnly.IZendiator")!;
        Assert.Single(classes.GetMembers("StreamAsync")); Assert.Single(classes.GetMembers("Send")); Assert.Single(classes.GetMembers("SendAsync"));
        Assert.Equal(2, output.GetTypeByMetadataName("ValuePublic.Generated.ValueMany.IZendiator")!.GetMembers("Send").Length);
        Assert.Equal(2, output.GetTypeByMetadataName("ValuePublic.Generated.ValueStreams.IZendiator")!.GetMembers("StreamAsync").Length);
        Assert.Equal(2, output.GetTypeByMetadataName("ValuePublic.Generated.TwoValueStreams.IZendiator")!.GetMembers("StreamAsync").Length);
        foreach (var name in new[] { "GenericMixed" })
        {
            var ns = "ValuePublic.Generated." + name;
            Assert.Null(output.GetTypeByMetadataName(ns + ".IZendiator"));
            Assert.Null(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
            Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) &&
                f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        }
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-value-gate", "gate-result.json"), JsonSerializer.Serialize(new
        {
            generatorInputs = 1,
            runtimeOperations = 0,
            accepted = new[] { "Single", "Mixed", "ClassOnly", "ValueSendOnly", "ValueAsync", "ValueMany", "ValueStreams", "TwoValueStreams" },
            rejected = new[] { "GenericMixed" },
            classOnlyIncludesSendAsyncAndThreeRoutes = true,
            reused = "v39 saved request copy/DI/IL/four-operation evidence"
        }));
    }

}

