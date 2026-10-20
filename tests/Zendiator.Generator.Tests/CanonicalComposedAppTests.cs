using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string ComposedSample()
    {
        using var stream = typeof(GeneratorTests).Assembly.GetManifestResourceStream("Zendiator.ComposedApp.Source")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    private static BlockSyntax ComposedRegistrationBody(string source) => Assert.IsType<BlockSyntax>(Assert.Single(
        CSharpSyntaxTree.ParseText(source, SyncParseOptions).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>(),
        n => n.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "AddZendiator" } })
        .ArgumentList.Arguments.Select(a => a.Expression).OfType<LambdaExpressionSyntax>().Single().Body);

    private static string[] ComposedRegistrationGroups(string source)
    {
        var groups = new List<string>();
        foreach (var statement in ComposedRegistrationBody(source).Statements)
        {
            var call = Assert.IsType<InvocationExpressionSyntax>(Assert.IsType<ExpressionStatementSyntax>(statement).Expression);
            var member = Assert.IsType<MemberAccessExpressionSyntax>(call.Expression);
            if (member.Name.Identifier.ValueText == "Handlers") groups.Add(statement.ToFullString());
            else groups[^1] += statement.ToFullString();
        }
        Assert.Equal(7, groups.Count); return groups.ToArray();
    }

    private static string ComposedDeclarations(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, SyncParseOptions).GetCompilationUnitRoot();
        return root.RemoveNodes(root.DescendantNodes().Where(n =>
            n is ClassDeclarationSyntax { Identifier.ValueText: "Program" } ||
            n is UsingDirectiveSyntax u && u.Name?.ToString() == "ComposedApp.Generated.App"), SyntaxRemoveOptions.KeepNoTrivia)!.ToFullString();
    }

    private static string ComposedReferences(string[] groups) => "using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;namespace ComposedApp {" +
        string.Join("\n", groups.Select((body, i) => $$"""
            [ZendiatorComposition("composed-reference-{{i}}")]public sealed class Reference{{i}};
            public static class Reference{{i}}Services {
                public static IServiceCollection Add(IServiceCollection services)=>services.AddZendiator<Reference{{i}}>(configuration=>{ {{body}} });
            }
            """)) + "}";

    private static string? ComposedFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "composed-app-" + name); Directory.CreateDirectory(folder); return folder;
    }

    private static void AssertComposedProfile(Compilation output, string ns, params (string Method, int Count)[] methods)
    {
        var api = output.GetTypeByMetadataName(ns + ".IZendiator"); Assert.NotNull(api);
        Assert.NotNull(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
        foreach (var (method, count) in methods) Assert.Equal(count, api.GetMembers(method).Length);
    }

    private const string ComposedRuntime = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Threading.Tasks.Sources;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace ComposedApp {
        public interface ICounted {int Consumptions{get;}void Complete();}
        public sealed class Counted<T>:IValueTaskSource<T>,ICounted {
            private ManualResetValueTaskSourceCore<T> core;private readonly T value;private readonly AppProbe p;private readonly string label;public int Consumptions{get;private set;}
            public Counted(AppProbe p,T value,string label){this.p=p;this.value=value;this.label=label;core.RunContinuationsAsynchronously=true;}
            public ValueTask<T> Task=>new(this,core.Version);public void Complete()=>core.SetResult(value);
            public T GetResult(short token){AppProbe.Check(++Consumptions==1,"one source consumption");var result=core.GetResult(token);p.Trace.Add("consume:"+label+":"+AppProbe.Ambient.Value);return result;}
            public ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        public sealed class CountedRefresh:IValueTaskSource,ICounted {
            private ManualResetValueTaskSourceCore<bool> core;private readonly AppProbe p;private readonly int value;public int Consumptions{get;private set;}
            public CountedRefresh(AppProbe p,int value){this.p=p;this.value=value;core.RunContinuationsAsynchronously=true;}
            public ValueTask Task=>new(this,core.Version);public void Complete()=>core.SetResult(true);
            public void GetResult(short token){AppProbe.Check(++Consumptions==1,"one native void consumption");_=core.GetResult(token);p.Count=value;p.RefreshCount++;p.Trace.Add("consume:refresh:"+AppProbe.Ambient.Value);}
            public ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        public sealed class AppProbe {
            public static readonly AsyncLocal<string?> Ambient=new();public string Step="";public CancellationToken Token;public int Count=2,RefreshCount,PrimaryCalls,ReplicaCalls;
            public bool Transient,PendingPrimary,PendingReplica,FailCached;public CancellationTokenSource? CancelPrimary;
            public readonly Product Product=new("p-7","Keyboard");public readonly Exception Failure=new FormatException("first branch");
            public readonly List<string> Trace=new(),Created=new(),Disposed=new();public readonly List<ICounted> Sources=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Create(string name){Created.Add(name);Trace.Add("create:"+name+":"+Ambient.Value);Ambient.Value="resolve";}
            public void Hit(string role,CancellationToken token){Check(token==Token,"token identity");Trace.Add("call:"+Step+":"+role+":"+Ambient.Value);Ambient.Value="callback:"+role;}
            public void Dispose(string name){Disposed.Add(name);Trace.Add("dispose:"+name);}
            public ValueTask<int> Value(int value,string role,bool pending){var source=new Counted<int>(this,value,Step+":"+role);Sources.Add(source);if(!pending)source.Complete();return source.Task;}
        }
        public sealed class ObservedStore:IEntityStore<Product>,IDisposable {
            private readonly AppProbe p;public ObservedStore(AppProbe p){this.p=p;p.Create("store");}
            public ValueTask<Product> FindAsync(string id,CancellationToken token){AppProbe.Check(id==p.Product.Id,"typed lookup key");p.Hit("store",token);var source=new Counted<Product>(p,p.Product,"lookup");p.Sources.Add(source);source.Complete();return source.Task;}
            public void Dispose()=>p.Dispose("store");
        }
        public sealed class ObservedInventory:IInventory,IDisposable {
            private readonly AppProbe p;public ObservedInventory(AppProbe p){this.p=p;p.Create("inventory");}
            public int CachedCount {get{p.Hit("cached",p.Token);if(p.FailCached)throw p.Failure;return p.Count;}}
            public int WarehouseCount {get{p.Hit("warehouse",p.Token);return p.Count+1;}}
            public ValueTask RefreshAsync(int count,CancellationToken token){p.Hit("refresh",token);var source=new CountedRefresh(p,count);p.Sources.Add(source);return source.Task;}
            public ValueTask<int> ReadPrimaryAsync(CancellationToken token){p.PrimaryCalls++;p.Hit("primary",token);if(p.Transient){p.Transient=false;return ValueTask.FromException<int>(new TransientCountException());}if(p.CancelPrimary is {} cancel){cancel.Cancel();return ValueTask.FromCanceled<int>(token);}return p.Value(p.Count,"primary",p.PendingPrimary);}
            public ValueTask<int> ReadReplicaAsync(CancellationToken token){p.ReplicaCalls++;p.Hit("replica",token);return p.Value(p.Count+1,"replica",p.PendingReplica);}
            public void Dispose()=>p.Dispose("inventory");
        }
        public static class ComposedEntry {
            private static void CheckSnapshot(){var configuration=new ZendiatorConfiguration();__SNAPSHOT__ configuration.Pipeline(typeof(Lookup<>),DispatchFamily.SendAsync);IServiceCollection rejected=new ServiceCollection();var stopped=false;try{Generated.App.ZendiatorGeneratedRegistrar.Add(rejected,configuration.Snapshot());}catch(InvalidOperationException){stopped=true;}AppProbe.Check(stopped&&rejected.Count==0,"whole snapshot checked before any descriptor");}
            private static async Task<string> Observe(bool composed){
                var p=new AppProbe();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddScoped<IInventory,ObservedInventory>();services.AddScoped<IEntityStore<Product>,ObservedStore>();services.AddScoped(typeof(LookupHandler<>));var existing=services.Last();
                void Register(){if(composed)AppServices.Add(services);else{Reference0Services.Add(services);Reference1Services.Add(services);Reference2Services.Add(services);Reference3Services.Add(services);Reference4Services.Add(services);Reference5Services.Add(services);Reference6Services.Add(services);}}
                Register();var count=services.Count;Register();AppProbe.Check(services.Count==count&&services.Contains(existing),"TryAdd and repeat registration");
                AppProbe.Check(services.Where(d=>d.ServiceType!=typeof(AppProbe)&&d.ServiceType.IsVisible).All(d=>d.Lifetime==ServiceLifetime.Scoped),"public scoped services and facades");
                var descriptors=services.Where(d=>d.ServiceType.Namespace?.StartsWith("ComposedApp.Generated.",StringComparison.Ordinal)!=true).Select(d=>d.ServiceType+"|"+d.Lifetime+"|"+d.ImplementationType).ToArray();
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var app=composed?sp.GetRequiredService<Generated.App.IZendiator>():null;if(composed)AppProbe.Check(ReferenceEquals(app,sp.GetRequiredService<Generated.App.Zendiator>()),"one combined facade");
                ValueTask<Product> Find(Lookup<Product> r,CancellationToken t)=>composed?app!.SendAsync(r,t):sp.GetRequiredService<Generated.Reference0.IZendiator>().SendAsync(r,t);
                ValueTask Refresh(RefreshInventory r,CancellationToken t)=>composed?app!.SendAsync(r,t):sp.GetRequiredService<Generated.Reference1.IZendiator>().SendAsync(r,t);
                int Sum(Sum<Product> r,CancellationToken t)=>composed?app!.Send(r,t):sp.GetRequiredService<Generated.Reference2.IZendiator>().Send(r,t);
                ValueTask<int> Cached(CachedCount<Product> r,CancellationToken t)=>composed?app!.SendAsync(r,t):sp.GetRequiredService<Generated.Reference3.IZendiator>().SendAsync(r,t);
                int[] Collect(CollectCounts<Product> r,CancellationToken t)=>composed?app!.SendAll(r,t):sp.GetRequiredService<Generated.Reference4.IZendiator>().SendAll(r,t);
                int CollectTo(CollectCounts<Product> r,int[] values,CancellationToken t)=>composed?app!.SendAll(r,values.AsSpan(),t):sp.GetRequiredService<Generated.Reference4.IZendiator>().SendAll(r,values.AsSpan(),t);
                ValueTask<int[]> Read(ReadCounts<Product> r,CancellationToken t)=>composed?app!.SendAllAsync(r,t):sp.GetRequiredService<Generated.Reference5.IZendiator>().SendAllAsync(r,t);
                IAsyncEnumerable<int> Stream(ReadNumbers r,CancellationToken t)=>composed?app!.StreamAsync(r,t):sp.GetRequiredService<Generated.Reference6.IZendiator>().StreamAsync(r,t);
                using var normal=new CancellationTokenSource();var attempts=0;void Step(string name,CancellationToken token){attempts++;p.Step=name;p.Token=token;AppProbe.Ambient.Value="caller";}
                Step("lookup",normal.Token);Product product=await Find(new(p.Product.Id),normal.Token);AppProbe.Check(ReferenceEquals(product,p.Product),"typed result instance");
                Step("refresh",normal.Token);var update=Refresh(new(3),normal.Token);AppProbe.Check(!update.IsCompleted&&p.Count==2&&p.RefreshCount==0&&p.Sources.Last().Consumptions==0,"pending native void has no eager state update");p.Sources.Last().Complete();await update.ConfigureAwait(false);AppProbe.Check(p.Count==3&&p.RefreshCount==1,"awaited update visible to other routes");
                Step("sum",normal.Token);var sum=Sum(new(20,21),normal.Token);AppProbe.Check(sum==42,"sync hook selected");
                Step("cache-miss",normal.Token);p.Transient=true;p.PendingPrimary=true;var miss=Cached(new("current"),normal.Token);AppProbe.Check(!miss.IsCompleted&&p.PrimaryCalls==2&&p.Sources.Last().Consumptions==0,"opaque wrapper retries next");p.Sources.Last().Complete();var cached=await miss.ConfigureAwait(false);p.PendingPrimary=false;AppProbe.Check(cached==3,"wrapper result");
                Step("cache-hit",normal.Token);var events=p.Trace.Count;var hit=await Cached(new("current"),normal.Token);AppProbe.Check(hit==3&&p.PrimaryCalls==2&&p.Trace.Count==events,"opaque cache returns without next");
                Step("collect",normal.Token);var collectRequest=new CollectCounts<Product>(1);var collected=Collect(collectRequest,normal.Token);AppProbe.Check(collected.SequenceEqual(new[]{15,16})&&collectRequest.Offset==1,"ordered sync branches start from original request");
                Step("collect-failure",normal.Token);p.FailCached=true;var destination=new[]{-9,-9,-9};var beforeWarehouse=p.Trace.Count(s=>s.Contains(":warehouse:"));try{CollectTo(new(1),destination,normal.Token);throw new InvalidOperationException("expected first branch failure");}catch(FormatException error){AppProbe.Check(ReferenceEquals(error,p.Failure),"failure identity");}AppProbe.Check(destination.All(v=>v==-9)&&p.Trace.Count(s=>s.Contains(":warehouse:"))==beforeWarehouse,"no later branch or partial Span commit");p.FailCached=false;
                Step("read",normal.Token);p.PendingReplica=true;var reading=Read(new(1),normal.Token);AppProbe.Check(!reading.IsCompleted&&p.ReplicaCalls==1&&p.Sources.Last().Consumptions==0,"genuine pending second branch");p.Sources.Last().Complete();var read=await reading.ConfigureAwait(false);p.PendingReplica=false;AppProbe.Check(read.SequenceEqual(new[]{25,26}),"ordered async branches start from original request");
                using var canceledBranch=new CancellationTokenSource();Step("read-cancel",canceledBranch.Token);p.CancelPrimary=canceledBranch;var replicas=p.ReplicaCalls;try{await Read(new(1),canceledBranch.Token).ConfigureAwait(false);throw new InvalidOperationException("expected branch cancellation");}catch(OperationCanceledException error){AppProbe.Check(error.CancellationToken==canceledBranch.Token,"branch cancellation token");}AppProbe.Check(p.ReplicaCalls==replicas,"cancel stops later branch");p.CancelPrimary=null;
                Step("stream",normal.Token);events=p.Trace.Count;var stream=Stream(new(6),normal.Token);AppProbe.Check(p.Trace.Count==events,"Stream call is lazy");var numbers=new List<int>();await using(var cursor=stream.GetAsyncEnumerator()){AppProbe.Check(p.Trace.Count==events,"enumerator acquisition is lazy");while(await cursor.MoveNextAsync())numbers.Add(cursor.Current);}AppProbe.Check(numbers.SequenceEqual(new[]{20,40}),"Stream filter/map order");
                using var canceledStream=new CancellationTokenSource();canceledStream.Cancel();Step("stream-cancel",canceledStream.Token);events=p.Trace.Count;await using(var cursor=Stream(new(6),canceledStream.Token).GetAsyncEnumerator()){AppProbe.Check(p.Trace.Count==events,"canceled Stream remains lazy");try{await cursor.MoveNextAsync();throw new InvalidOperationException("expected Stream cancellation");}catch(OperationCanceledException error){AppProbe.Check(error.CancellationToken==canceledStream.Token,"Stream cancellation token");}}AppProbe.Check(p.Trace.Count==events,"canceled Stream activates no source");
                AppProbe.Check(attempts==11&&p.Sources.Count==5&&p.Sources.All(s=>s.Consumptions==1)&&p.Disposed.Count==0,"one consumption and borrowed lifetime");scope.Dispose();AppProbe.Check(p.Created.SequenceEqual(new[]{"store","inventory"})&&p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope owns dependencies exactly once");
                return JsonSerializer.Serialize(new{attempts,product=product.Name,sum,cached,hit,collected,read,numbers,p.Count,p.RefreshCount,p.PrimaryCalls,p.ReplicaCalls,p.Trace,p.Created,p.Disposed,consumptions=p.Sources.Select(s=>s.Consumptions),descriptors});
            }
            public static async Task<string> Run(){CheckSnapshot();var reference=await Observe(false);var composed=await Observe(true);AppProbe.Check(reference==composed,"single backend/composed App mismatch\n"+reference+"\n"+composed);using var r=JsonDocument.Parse(reference);using var c=JsonDocument.Parse(composed);return JsonSerializer.Serialize(new{pairs=1,dispatchAttempts=22,snapshotRejectedBeforeDescriptors=true,reference=r.RootElement.Clone(),composed=c.RootElement.Clone()});}
        }
        }
        """;

    [Fact]
    public async Task Composed_app_reuses_single_route_backends_preserves_route_outputs_and_observable_contracts()
    {
        var sample = ComposedSample(); var groups = ComposedRegistrationGroups(sample); var references = ComposedReferences(groups);
        var declarations = ComposedDeclarations(sample); var body = ComposedRegistrationBody(declarations);
        var beforeSource = declarations.Replace(body.ToFullString(), "{" + string.Join("\n", groups.Take(4)) + "}");
        var compilation = SyncCompilation(beforeSource, "ComposedApp_" + Guid.NewGuid().ToString("N")).AddSyntaxTrees(CSharpSyntaxTree.ParseText(references, SyncParseOptions));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var beforeOutput, out var beforeDiagnostics); SaveSendAllSync(ComposedFolder("before"), beforeSource, driver, beforeOutput, beforeDiagnostics);
        Assert.Empty(beforeDiagnostics); Assert.Empty(beforeOutput.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var stable = driver.GetRunResult().Results.Single().GeneratedSources.Where(s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal) && s.SourceText.ToString().Contains("namespace ComposedApp.Generated.App")).ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal); Assert.Equal(4, stable.Count);
        var runtimeSource = ComposedRuntime.Replace("__SNAPSHOT__", string.Join("\n", groups));
        var finalCompilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(sample, SyncParseOptions)).AddSyntaxTrees(CSharpSyntaxTree.ParseText(runtimeSource, SyncParseOptions));
        var finalDriver = driver.RunGeneratorsAndUpdateCompilation(finalCompilation, out var output, out var diagnostics); var folder = ComposedFolder("runtime"); SaveSendAllSync(folder, sample, finalDriver, output, diagnostics);
        if (folder != null) { File.WriteAllText(Path.Combine(folder, "input-0.cs"), sample); File.WriteAllText(Path.Combine(folder, "input-1.cs"), references); File.WriteAllText(Path.Combine(folder, "input-2.cs"), runtimeSource); }
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var sources = finalDriver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal); foreach (var source in stable) Assert.Equal(source.Value, sources[source.Key]);
        Assert.Equal(7, sources.Count(s => s.Key.StartsWith("Zendiator.Route.", StringComparison.Ordinal) && s.Value.Contains("namespace ComposedApp.Generated.App")));
        var api = output.GetTypeByMetadataName("ComposedApp.Generated.App.IZendiator")!; Assert.NotNull(api); Assert.False(api.IsGenericType); Assert.Equal(3, api.GetMembers("SendAsync").Length); Assert.Single(api.GetMembers("Send")); Assert.Equal(2, api.GetMembers("SendAll").Length); Assert.Single(api.GetMembers("SendAllAsync")); Assert.Single(api.GetMembers("StreamAsync"));
        var lookup = Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>(), m => m.Parameters[0].Type.Name == "Lookup"); var parameter = Assert.Single(lookup.TypeParameters); Assert.True(parameter.HasReferenceTypeConstraint); Assert.Contains(parameter.ConstraintTypes, t => t.Name == "IEntity"); Assert.True(SymbolEqualityComparer.Default.Equals(parameter, Assert.Single(Assert.IsAssignableFrom<INamedTypeSymbol>(lookup.ReturnType).TypeArguments)));
        var command = Assert.Single(api.GetMembers("SendAsync").OfType<IMethodSymbol>(), m => m.Parameters[0].Type.Name == "RefreshInventory"); Assert.True(SymbolEqualityComparer.Default.Equals(output.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"), command.ReturnType));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var runtime = await assembly.GetType("ComposedApp.ComposedEntry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(25));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime); using var json = JsonDocument.Parse(runtime); Assert.Equal(22, json.RootElement.GetProperty("dispatchAttempts").GetInt32());
    }

    [Fact]
    public void Composed_app_rejects_invalid_or_unadopted_routes_without_partial_generation()
    {
        var declarations = ComposedDeclarations(ComposedSample());
        const string collect = "typeof(CachedCollector<>), typeof(WarehouseCollector<>)";
        const string extra = "public sealed record Extra : IRequest<int>;[ZendiatorHandler]public sealed class ExtraHandler:IRequestHandler<Extra,int>{public ValueTask<int> HandleAsync(Extra request,CancellationToken token)=>new(0);}";
        foreach (var (name, source, expected) in new[]
        {
            ("unbound", declarations.Replace("class LookupHandler<T>", "class LookupHandler<T, Unused>").Replace("typeof(LookupHandler<>)", "typeof(LookupHandler<,>)"), "ZEN0035"),
            ("ambiguous", declarations.Replace("CachedCount<TMarker>(string Key) : IRequest<int>", "CachedCount<TMarker>(string Key) : IRequest<int>, IRequest<object>"), "ZEN0035"),
            ("overlap", declarations.Replace("configuration.Handlers<RefreshInventory>", "configuration.Handlers<Lookup<Product>>(DispatchFamily.SendAsync, typeof(LookupHandler<Product>));configuration.Handlers<RefreshInventory>"), "ZEN0028"),
            ("orphan", declarations.Replace("configuration.Handlers<RefreshInventory>", "configuration.AddHandler(typeof(ExtraHandler));configuration.Handlers<RefreshInventory>") + extra, "ZEN0037"),
            ("unadopted-three-branches", declarations.Replace(collect, collect + ", typeof(ThirdCollector<>)") + "public sealed class ThirdCollector<TMarker>:ISyncRequestHandler<CollectCounts<TMarker>,int> where TMarker:class{public int Handle(CollectCounts<TMarker> request,CancellationToken token)=>0;}".Replace("public sealed class", "[ZendiatorHandler]public sealed class"), "ZEN0035"),
            ("invalid-factory", declarations.Replace("typeof(ReadNumbersHandler));", "HandlerEntry.Session(typeof(ReadNumbersHandler), typeof(InvalidFactory)));") + "public sealed class InvalidFactory;", "ZEN0025"),
        })
        {
            Assert.NotEqual(declarations, source); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ComposedRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); SaveSendAllSync(ComposedFolder(name), source, driver, output, diagnostics);
            Assert.Contains(diagnostics, d => d.Id == expected && d.Location.IsInSource); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            Assert.Null(output.GetTypeByMetadataName("ComposedApp.Generated.App.IZendiator")); Assert.Null(output.GetTypeByMetadataName("ComposedApp.Generated.App.ZendiatorGeneratedRegistrar")); Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal));
        }
    }
}
