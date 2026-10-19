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
    private static string TypedLookupSample()
    {
        using var stream = typeof(GeneratorTests).Assembly.GetManifestResourceStream("Zendiator.GenericLookup.Source")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    private static string TypedLookupDeclarations(string sample)
    {
        var root = CSharpSyntaxTree.ParseText(sample, SyncParseOptions).GetCompilationUnitRoot();
        var remove = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText is "Program" or "InMemoryEntityStore").Cast<SyntaxNode>()
            .Concat(root.Usings.Where(u => u.Name?.ToString() == "GenericLookup.Generated.App"));
        return root.RemoveNodes(remove, SyntaxRemoveOptions.KeepNoTrivia)!.ToFullString();
    }

    private const string TypedLookupRuntime = """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Text.Json;
        using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace GenericLookup {
        public interface ICountedValue {int Consumptions{get;}void Complete();}
        public sealed class CountedValue<T>:System.Threading.Tasks.Sources.IValueTaskSource<T>,ICountedValue where T:class,IEntity {
            private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<T> core;private readonly T value;private readonly LookupProbe p;private readonly string step;
            public int Consumptions{get;private set;}public CountedValue(T value,LookupProbe p){this.value=value;this.p=p;step=p.Step;core.RunContinuationsAsynchronously=true;}
            public ValueTask<T> Task=>new(this,core.Version);public void Complete()=>core.SetResult(value);
            public T GetResult(short token){LookupProbe.Check(++Consumptions==1,"one typed ValueTask consumption");LookupProbe.Check(LookupProbe.Ambient.Value=="callback:"+typeof(T).Name,"typed async execution context");p.Trace.Add("consume:"+step);return core.GetResult(token);}
            public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)=>core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation,object? state,short token,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)=>core.OnCompleted(continuation,state,token,flags);
        }
        public sealed class LookupProbe {
            public static readonly AsyncLocal<string?> Ambient=new();public readonly Product Product=new("p-7","Keyboard");public readonly Customer Customer=new("c-3","Ada");
            public string Step="",ExpectedId="";public CancellationToken Token;public bool Pending;
            public readonly List<string> Steps=new(),Trace=new();public readonly List<string?> AmbientSeen=new();public readonly List<object> Created=new(),Disposed=new();public readonly List<ICountedValue> Sources=new();
            public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
            public void Resolve(object receiver){Created.Add(receiver);Trace.Add("resolve:"+receiver.GetType()+":"+Ambient.Value);Ambient.Value="resolve";}
            public void Hit(string role){Check(!Steps.Contains(Step),"single Handler call");Steps.Add(Step);AmbientSeen.Add(Ambient.Value);Trace.Add("call:"+Step+":"+role+":"+Ambient.Value);Ambient.Value="callback:"+role;}
            public void Dispose(object receiver){Disposed.Add(receiver);Trace.Add("dispose:"+receiver.GetType());}
        }
        public sealed class ProbeEntityStore<T>:IEntityStore<T>,IDisposable where T:class,IEntity {
            private readonly LookupProbe p;public ProbeEntityStore(LookupProbe p){this.p=p;p.Resolve(this);}
            public ValueTask<T> FindAsync(string id,CancellationToken token){
                LookupProbe.Check(ReferenceEquals(id,p.ExpectedId)&&token==p.Token,"lookup key/token");p.Hit(typeof(T).Name);
                IEntity entity=typeof(T)==typeof(Product)?p.Product:typeof(T)==typeof(Customer)?p.Customer:throw new InvalidOperationException("unstarted lookup resolved");
                var source=new CountedValue<T>((T)entity,p);p.Sources.Add(source);if(!p.Pending)source.Complete();return source.Task;
            }
            public void Dispose()=>p.Dispose(this);
        }
        public sealed class ProbeStatus: IStatusSource,IDisposable {
            private readonly LookupProbe p;public ProbeStatus(LookupProbe p){this.p=p;p.Resolve(this);}public int ActiveCount{get{p.Hit("status");return 2;}}public void Dispose()=>p.Dispose(this);
        }
        public sealed record UnopenedEntity(string Id):IEntity;
        public static class TypedEntry {
            private static ValueTask<T> Reference<T>(IServiceProvider provider,Lookup<T> request,CancellationToken token) where T:class,IEntity {ArgumentNullException.ThrowIfNull(request);return Core(provider,request,token);}
            private static async ValueTask<T> Core<T>(IServiceProvider provider,Lookup<T> request,CancellationToken token) where T:class,IEntity {token.ThrowIfCancellationRequested();return await provider.GetRequiredService<LookupHandler<T>>().HandleAsync(request,token).ConfigureAwait(false);}
            private static ValueTask<int> Reference(IServiceProvider provider,StatusQuery request,CancellationToken token){ArgumentNullException.ThrowIfNull(request);return Core(provider,request,token);}
            private static async ValueTask<int> Core(IServiceProvider provider,StatusQuery request,CancellationToken token){token.ThrowIfCancellationRequested();return await provider.GetRequiredService<StatusHandler>().HandleAsync(request,token).ConfigureAwait(false);}
            private static async Task<string> Observe(bool generated){
                var p=new LookupProbe();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddScoped(typeof(IEntityStore<>),typeof(ProbeEntityStore<>));services.AddScoped<IStatusSource,ProbeStatus>();services.AddScoped(typeof(LookupHandler<>));var existing=services.Last();
                AppServices.Add(services);var count=services.Count;AppServices.Add(services);LookupProbe.Check(count==7&&services.Count==count&&services.Contains(existing),"scoped TryAdd registration");
                foreach(var type in new[]{typeof(LookupHandler<>),typeof(StatusHandler),typeof(Generated.App.IZendiator),typeof(Generated.App.Zendiator)})LookupProbe.Check(services.Single(d=>d.ServiceType==type).Lifetime==ServiceLifetime.Scoped,"Handler/facade scoped lifetime");
                var mismatch=new ZendiatorConfiguration();mismatch.Handlers(typeof(Lookup<>),DispatchFamily.SendAsync,typeof(LookupHandler<>));mismatch.Handlers<StatusQuery>(DispatchFamily.SendAsync,typeof(StatusHandler));mismatch.Pipeline(typeof(Lookup<>),DispatchFamily.SendAsync);mismatch.Pipeline<StatusQuery>(DispatchFamily.SendAsync);
                IServiceCollection rejected=new ServiceCollection();var mismatchRejected=false;try{Generated.App.ZendiatorGeneratedRegistrar.Add(rejected,mismatch.Snapshot());}catch(InvalidOperationException){mismatchRejected=true;}LookupProbe.Check(mismatchRejected&&rejected.Count==0,"omitted snapshot stays omitted");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();var sp=scope.ServiceProvider;
                var mediator=sp.GetRequiredService<Generated.App.IZendiator>();LookupProbe.Check(ReferenceEquals(mediator,sp.GetRequiredService<Generated.App.Zendiator>()),"one facade instance");
                using var normal=new CancellationTokenSource();using var canceled=new CancellationTokenSource();canceled.Cancel();var attempts=0;var pendingCount=0;string? cancellationPhase=null;
                async Task<T> Find<T>(string step,string id,bool pending) where T:class,IEntity {
                    attempts++;p.Step=step;p.ExpectedId=id;p.Token=normal.Token;p.Pending=pending;LookupProbe.Ambient.Value="caller";var previous=p.Sources.Count;var request=new Lookup<T>(id);
                    var operation=generated?mediator.SendAsync(request,normal.Token):Reference(sp,request,normal.Token);LookupProbe.Check(LookupProbe.Ambient.Value=="caller"&&p.Sources.Count==previous+1,"typed call boundary");var source=p.Sources.Last();
                    if(pending){LookupProbe.Check(!operation.IsCompleted&&source.Consumptions==0,"genuine pending typed source");pendingCount++;source.Complete();}else LookupProbe.Check(operation.IsCompletedSuccessfully&&source.Consumptions==1,"immediate typed source");
                    T result=await operation.ConfigureAwait(false);LookupProbe.Check(source.Consumptions==1&&LookupProbe.Ambient.Value=="caller","typed return/one consumption/context");return result;
                }
                async Task<int> Status(string step){attempts++;p.Step=step;p.Token=normal.Token;LookupProbe.Ambient.Value="caller";var request=new StatusQuery();var operation=generated?mediator.SendAsync(request,normal.Token):Reference(sp,request,normal.Token);LookupProbe.Check(LookupProbe.Ambient.Value=="caller","closed call context");var result=await operation.ConfigureAwait(false);LookupProbe.Check(result==2&&LookupProbe.Ambient.Value=="caller","closed status/context");return result;}
                Product first=await Find<Product>("product-1",p.Product.Id,false);var productHandler=sp.GetRequiredService<LookupHandler<Product>>();
                var status=await Status("status-1");var statusHandler=sp.GetRequiredService<StatusHandler>();
                Customer second=await Find<Customer>("customer",p.Customer.Id,true);Product again=await Find<Product>("product-repeat",p.Product.Id,false);
                attempts++;p.Step="canceled";LookupProbe.Ambient.Value="caller";var eventCount=p.Trace.Count;var sourceCount=p.Sources.Count;var stopped=new Lookup<UnopenedEntity>("never");
                var cancellation=generated?mediator.SendAsync(stopped,canceled.Token):Reference(sp,stopped,canceled.Token);
                LookupProbe.Check(cancellation.IsCanceled&&p.Trace.Count==eventCount&&p.Sources.Count==sourceCount&&LookupProbe.Ambient.Value=="caller","cancel before Handler/store resolution");
                try{_=await cancellation.ConfigureAwait(false);throw new InvalidOperationException("expected cancellation");}catch(OperationCanceledException error){LookupProbe.Check(error.CancellationToken==canceled.Token,"cancellation token identity");cancellationPhase="await";}
                var after=await Status("status-after-cancel");
                LookupProbe.Check(ReferenceEquals(first,p.Product)&&ReferenceEquals(again,p.Product)&&ReferenceEquals(second,p.Customer),"typed result instance identity");
                LookupProbe.Check(ReferenceEquals(productHandler,sp.GetRequiredService<LookupHandler<Product>>())&&ReferenceEquals(statusHandler,sp.GetRequiredService<StatusHandler>()),"same scoped Handler instances");
                LookupProbe.Check(p.Steps.SequenceEqual(new[]{"product-1","status-1","customer","product-repeat","status-after-cancel"})&&p.Sources.Count==3&&p.Sources.All(s=>s.Consumptions==1),"route order/one invocation/typed consumption");
                LookupProbe.Check(p.Created.Select(r=>r.GetType()).SequenceEqual(new[]{typeof(ProbeEntityStore<Product>),typeof(ProbeStatus),typeof(ProbeEntityStore<Customer>)}),"typed DI keys/no canceled store");
                LookupProbe.Check(p.AmbientSeen.SequenceEqual(new[]{"resolve","resolve","resolve","caller","caller"})&&p.Disposed.Count==0,"caller context/borrowed dependencies survive dispatch");scope.Dispose();LookupProbe.Check(p.Disposed.SequenceEqual(p.Created.AsEnumerable().Reverse()),"scope owns dependencies exactly once");
                LookupProbe.Check(first.Name=="Keyboard"&&second.Name=="Ada","returned values remain usable");
                return JsonSerializer.Serialize(new{attempts,pendingCount,cancellationPhase,status,after,product=first.Name,customer=second.Name,instancesPreserved=true,p.Steps,p.Trace,p.AmbientSeen,consumptions=p.Sources.Select(s=>s.Consumptions),created=p.Created.Select(r=>r.GetType().ToString()),disposed=p.Disposed.Select(r=>r.GetType().ToString()),registrations=count,mismatchRejected});
            }
            public static async Task<string> Run(){var reference=await Observe(false);var generated=await Observe(true);LookupProbe.Check(reference==generated,"reference/generated mismatch\n"+reference+"\n"+generated);using var r=JsonDocument.Parse(reference);using var g=JsonDocument.Parse(generated);return JsonSerializer.Serialize(new{pairs=1,dispatchAttempts=r.RootElement.GetProperty("attempts").GetInt32()+g.RootElement.GetProperty("attempts").GetInt32(),callbackInvocations=r.RootElement.GetProperty("Steps").GetArrayLength()+g.RootElement.GetProperty("Steps").GetArrayLength(),reference=r.RootElement.Clone(),generated=g.RootElement.Clone()});}
        }
        }
        """;

    private const string TypedLookupEmptyCheck = """
        using System;using System.Linq;using System.Text.Json;using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
        namespace GenericLookupEmpty {
        public static class EmptyEntry {
            public static string Run(){IServiceCollection services=new ServiceCollection();services.AddScoped(typeof(IEntityStore<>),typeof(InMemoryEntityStore<>));services.AddSingleton<IStatusSource>(new InventoryStatus(2));AppServices.Add(services);var count=services.Count;AppServices.Add(services);if(count!=6||services.Count!=count)throw new InvalidOperationException("empty repeat registration");
                var omitted=new ZendiatorConfiguration();omitted.Handlers(typeof(Lookup<>),DispatchFamily.SendAsync,typeof(LookupHandler<>));omitted.Handlers<StatusQuery>(DispatchFamily.SendAsync,typeof(StatusHandler));IServiceCollection rejected=new ServiceCollection();var mismatch=false;try{Generated.App.ZendiatorGeneratedRegistrar.Add(rejected,omitted.Snapshot());}catch(InvalidOperationException){mismatch=true;}if(!mismatch||rejected.Count!=0)throw new InvalidOperationException("explicit empty snapshot");
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});using var scope=provider.CreateScope();if(!ReferenceEquals(scope.ServiceProvider.GetRequiredService<Generated.App.IZendiator>(),scope.ServiceProvider.GetRequiredService<Generated.App.Zendiator>()))throw new InvalidOperationException("empty facade alias");return JsonSerializer.Serialize(new{registrations=count,mismatchRejected=mismatch,dispatchAttempts=0});}
        }
        }
        """;

    private static string? TypedLookupFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "typed-lookup-" + name); Directory.CreateDirectory(folder); return folder;
    }

    [Fact]
    public async Task OpenSend_typed_response_uses_shared_sample_and_preserves_typed_instances_and_scope()
    {
        var sample = TypedLookupSample();
        const string closed = "configuration.Handlers<StatusQuery>(DispatchFamily.SendAsync, typeof(StatusHandler));";
        var empty = sample.Replace("GenericLookup", "GenericLookupEmpty").Replace("\"genericLookup\"", "\"genericLookupEmpty\"").Replace(closed, closed + "configuration.Pipeline(typeof(Lookup<>), DispatchFamily.SendAsync);configuration.Pipeline<StatusQuery>(DispatchFamily.SendAsync);");
        var inputs = new[] { sample, TypedLookupRuntime, empty, TypedLookupEmptyCheck, OpenSendZeroPairSource(consumer: false, sentinel: false), OpenSendWrapperPairSource(orphan: false, consumer: false), OpenSendZeroCommon + OpenSendZeroDefinition("AV") };
        var compilation = SyncCompilation(sample, "TypedLookup_" + Guid.NewGuid().ToString("N")).AddSyntaxTrees(inputs.Skip(1).Select(s => CSharpSyntaxTree.ParseText(s, SyncParseOptions)));
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics); var folder = TypedLookupFolder("runtime"); SaveSendAllSync(folder, sample, driver, output, diagnostics);
        if (folder != null) for (var i = 0; i < inputs.Length; i++) File.WriteAllText(Path.Combine(folder, "input-" + i + ".cs"), inputs[i]);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var model = CanonicalMultiEmissionNormalizer.Create(catalog, CanonicalExecutionGraphNormalizer.Create(catalog));
        foreach (var ns in new[] { "GenericLookup.Generated.App", "GenericLookupEmpty.Generated.App" })
        {
            var composition = Assert.Single(model.Compositions, c => c.Namespace == ns); Assert.Equal(2, composition.Routes.Count); Assert.True(CanonicalRegistrationPlan.Compatible(composition));
            var bound = Assert.Single(catalog.Compositions, c => c.GeneratedNamespace == ns); var open = Assert.Single(bound.Pipelines, r => r.RequestShape == BoundRequestShape.OpenAsyncReference); var handler = Assert.Single(open.Handlers);
            Assert.True(handler.RequestReferenceResponse); Assert.NotEqual(handler, handler with { RequestReferenceResponse = false }); Assert.Equal(Assert.Single(open.SendGeneric!.Parameters), handler.AsyncEmission!.ResultName); Assert.Empty(handler.Sessions); Assert.Empty(open.Stages);
            var api = output.GetTypeByMetadataName(ns + ".IZendiator")!; var mediator = output.GetTypeByMetadataName(ns + ".Zendiator")!; Assert.NotNull(api); Assert.NotNull(mediator); Assert.False(api.IsGenericType);
            foreach (var type in new[] { api, mediator })
            {
                var methods = type.GetMembers("SendAsync").OfType<IMethodSymbol>().ToArray(); Assert.Equal(2, methods.Length); var generic = Assert.Single(methods, m => m.Arity == 1); var status = Assert.Single(methods, m => m.Arity == 0);
                var parameter = Assert.Single(generic.TypeParameters); Assert.True(parameter.HasReferenceTypeConstraint); Assert.False(parameter.HasConstructorConstraint); Assert.Contains(parameter.ConstraintTypes, t => t.Name == "IEntity");
                var result = Assert.IsAssignableFrom<INamedTypeSymbol>(generic.ReturnType); Assert.Equal("ValueTask", result.Name); var argument = Assert.Single(result.TypeArguments); Assert.True(SymbolEqualityComparer.Default.Equals(parameter, argument)); Assert.NotEqual(NullableAnnotation.Annotated, argument.NullableAnnotation);
                Assert.Equal(SpecialType.System_Int32, Assert.Single(Assert.IsAssignableFrom<INamedTypeSymbol>(status.ReturnType).TypeArguments).SpecialType); Assert.All(methods, m => Assert.False(m.IsAsync));
            }
        }
        foreach (var ns in new[] { "OpenZeroPair.Generated.Omitted", "OpenSendWrapper.Generated.Pair", "OpenZero.AV.Generated.Omitted", "OpenZero.AV.Generated.Empty" }) Assert.NotNull(output.GetTypeByMetadataName(ns + ".IZendiator"));
        Assert.Null(output.GetTypeByMetadataName("OpenZeroPair.Entry")); Assert.Null(output.GetTypeByMetadataName("OpenSendWrapper.PairEntry"));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var runtime = await assembly.GetType("GenericLookup.TypedEntry")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        var registered = assembly.GetType("GenericLookupEmpty.EmptyEntry")!.GetMethod("Run")!.CreateDelegate<Func<string>>()();
        if (folder != null) { File.WriteAllText(Path.Combine(folder, "runtime.json"), runtime); File.WriteAllText(Path.Combine(folder, "empty-registration.json"), registered); }
        using var json = JsonDocument.Parse(runtime); Assert.Equal(12, json.RootElement.GetProperty("dispatchAttempts").GetInt32()); Assert.Equal(10, json.RootElement.GetProperty("callbackInvocations").GetInt32());
    }

    [Fact]
    public void OpenSend_typed_response_rejects_unproved_results_and_preserves_consumer_constraints()
    {
        var declarations = TypedLookupDeclarations(TypedLookupSample());
        var syncRoot = CSharpSyntaxTree.ParseText(declarations, SyncParseOptions).GetCompilationUnitRoot();
        var lookup = Assert.Single(syncRoot.DescendantNodes().OfType<ClassDeclarationSyntax>(), c => c.Identifier.ValueText == "LookupHandler"); var method = Assert.Single(lookup.Members.OfType<MethodDeclarationSyntax>());
        var syncMethod = method.WithIdentifier(SyntaxFactory.Identifier("Handle").WithTriviaFrom(method.Identifier)).WithReturnType(SyntaxFactory.ParseTypeName("T").WithTriviaFrom(method.ReturnType)).WithExpressionBody(null).WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("_ = store;"), SyntaxFactory.ParseStatement("throw new NotSupportedException();"))).WithSemicolonToken(default);
        var sync = syncRoot.ReplaceNode(method, syncMethod).ToFullString().Replace("IRequest<T>", "ISyncRequest<T>").Replace("IRequestHandler<Lookup<T>, T>", "ISyncRequestHandler<Lookup<T>, T>").Replace("typeof(Lookup<>), DispatchFamily.SendAsync", "typeof(Lookup<>), DispatchFamily.SendSync");
        foreach (var (name, source) in new[]
        {
            ("unconstrained", declarations.Replace("where T : class, IEntity", "").Replace("where T : class", "")),
            ("unused", declarations.Replace("public sealed class LookupHandler<T>", "public sealed class LookupHandler<T, Unused>").Replace("typeof(LookupHandler<>)", "typeof(LookupHandler<,>)")),
            ("sync", sync),
        })
        {
            var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "TypedLookupRejected_" + name + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); SaveSendAllSync(TypedLookupFolder(name), source, driver, output, diagnostics);
            Assert.Contains(diagnostics, d => d.Id == "ZEN0035"); Assert.All(diagnostics, d => Assert.Contains(d.Id, new[] { "ZEN0035", "ZEN0037" })); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
            Assert.Null(output.GetTypeByMetadataName("GenericLookup.Generated.App.IZendiator")); Assert.Null(output.GetTypeByMetadataName("GenericLookup.Generated.App.ZendiatorGeneratedRegistrar")); Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal));
        }
        var constraints = declarations + "public sealed class WrongEntity;public static class BadConsumer {public static ValueTask<WrongEntity> Send(Generated.App.IZendiator mediator)=>mediator.SendAsync(new Lookup<WrongEntity>(\"bad\"));}";
        var accepted = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(constraints, "TypedLookupCompiler_" + Guid.NewGuid().ToString("N")), out var compiler, out var generatorDiagnostics); SaveSendAllSync(TypedLookupFolder("constraints"), constraints, accepted, compiler, generatorDiagnostics); Assert.Empty(generatorDiagnostics);
        var error = Assert.Single(compiler.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error); Assert.Equal("CS0311", error.Id); Assert.Contains("WrongEntity", error.GetMessage()); Assert.True(error.Location.IsInSource); Assert.Equal("SendAsync", constraints.Substring(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length));
        var api = compiler.GetTypeByMetadataName("GenericLookup.Generated.App.IZendiator")!; Assert.NotNull(api); Assert.Equal(2, api.GetMembers("SendAsync").OfType<IMethodSymbol>().Count());
    }
}
