using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_owned_stream_metadata_opt_out_requires_the_actual_marker(bool actualMarker)
    {
        var producer = """
            using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
            """ + (actualMarker ? "" : """
            // Deliberately shadow the real marker in a different producer assembly.
            #pragma warning disable CS0436
            namespace Zendiator {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ZendiatorHandlerAttribute:Attribute { public bool OwnsStream {get;set;} }
            }
            """) + """
            namespace MetadataOwned {
                public sealed record Request:global::Zendiator.IStreamRequest<int>;
                public sealed class Probe {public int Created,Calls;}
                [global::Zendiator.ZendiatorHandlerAttribute(OwnsStream=false)]
                public sealed class Handler:global::Zendiator.IStreamRequestHandler<Request,int> {
                    private readonly Probe probe;
                    public Handler(Probe probe){this.probe=probe;probe.Created++;}
                    public IAsyncEnumerable<int> HandleAsync(Request request,CancellationToken token){probe.Calls++;return Sequence();}
                    private static async IAsyncEnumerable<int> Sequence(){await Task.CompletedTask;yield return 7;}
                }
            }
            """;
        var producerBytes = CompileExplicitMetadataProducer(producer, "owned-stream-marker-" + actualMarker);
        var reference = MetadataReference.CreateFromImage(producerBytes);
        var source = $$"""
            using System;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;
            namespace MetadataOwnedConsumer {
                [ZendiatorComposition("owned-stream-metadata")]public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<MetadataOwned.Request>(DispatchFamily.Stream,typeof(MetadataOwned.Handler));
                        c.Pipeline<MetadataOwned.Request>(DispatchFamily.Stream);
                    });
                    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
                    public static async Task Run(){
                        var probe=new MetadataOwned.Probe();IServiceCollection services=new ServiceCollection();services.AddSingleton(probe);Register(services);
                        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                        await using var scope=provider.CreateAsyncScope();
                        var mediator=scope.ServiceProvider.GetRequiredService<global::MetadataOwnedConsumer.Generated.Reads.IZendiator>();
                        var enumerable=mediator.StreamAsync(new MetadataOwned.Request());
                        var expected={{(actualMarker ? "0" : "1")}};
                        Check(probe.Created==expected&&probe.Calls==expected,"only the actual metadata marker may opt out of handler ownership");
                        await using var cursor=enumerable.GetAsyncEnumerator();
                        Check(await cursor.MoveNextAsync()&&cursor.Current==7,"metadata handler must remain executable");
                        Check(probe.Created==1&&probe.Calls==1,"first enumeration resolves and calls the handler once");
                    }
                }
            }
            """;
        var compilation = SyncCompilation(source, "OwnedStreamMetadata_" + Guid.NewGuid().ToString("N")).AddReferences(reference);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var connection = Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalStreamConnectionModel"]
            .SelectMany(step => step.Outputs).Select(result => result.Value).OfType<CanonicalStreamConnectionCatalog>()).Routes.Single();
        Assert.Equal(!actualMarker, connection.Kind == CanonicalStreamConnectionKind.HandlerOwned);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "owned-stream-metadata-" + actualMarker);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(path, generated.HintName), generated.SourceText.ToString());
        }
        using var producerStream = new MemoryStream(producerBytes);
        AssemblyLoadContext.Default.LoadFromStream(producerStream);
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        await ((Task)assembly.GetType("MetadataOwnedConsumer.Bridge")!.GetMethod("Run")!.Invoke(null, null)!).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handler_owned_stream_keeps_Mediator_entry_and_declares_its_acquisition_and_enumeration_contract(bool owns, bool staged)
    {
        var source = $$"""
            using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
            using System.Runtime.CompilerServices;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            namespace Owned {
                public sealed record Request:IStreamRequest<int>;
                public sealed class Probe { public int Created,Calls,Starts,SourceDisposals,HandlerDisposals; public object? Last; }
                [ZendiatorHandler{{(owns ? "" : "(OwnsStream=false)")}}]
                public sealed class Handler(Probe p):IStreamRequestHandler<Request,int>,IDisposable {
                    private readonly Probe _p=Created(p);
                    private static Probe Created(Probe p){p.Created++;return p;}
                    public IAsyncEnumerable<int> HandleAsync(Request request,CancellationToken token){_p.Calls++;var source=new OwnedSource(this,token);_p.Last=source;return source;}
                    private sealed class OwnedSource(Handler owner,CancellationToken api):IAsyncEnumerable<int>{
                        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken enumeration=default)=>owner.Sequence(api).GetAsyncEnumerator(enumeration);
                    }
                    private async IAsyncEnumerable<int> Sequence([EnumeratorCancellation]CancellationToken token){
                        await Task.CompletedTask; token.ThrowIfCancellationRequested();_p.Starts++;
                        try { for(var item=1;item<=2;item++){token.ThrowIfCancellationRequested();yield return item;} }
                        finally {_p.SourceDisposals++;}
                    }
                    public void Dispose()=>_p.HandlerDisposals++;
                }
                public sealed class Projection:IStreamItemMap<Request,int>{public int Map(Request r,int item,CancellationToken token)=>item;}
                [ZendiatorComposition("owned-stream")]public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<Request>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Request>(DispatchFamily.Stream{{(staged ? ",Stage.Hook<Projection>()" : "")}});
                    });
                    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
                    public static async Task Run(){
                        var probe=new Probe();IServiceCollection services=new ServiceCollection();services.AddSingleton(probe);Register(services);
                        await using(var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
                        await using(var scope=provider.CreateAsyncScope()){
                            var mediator=scope.ServiceProvider.GetRequiredService<global::Owned.Generated.Reads.IZendiator>();
                            using var api=new CancellationTokenSource();using var a=new CancellationTokenSource();using var b=new CancellationTokenSource();
                            var source=mediator.StreamAsync(new Request(),api.Token);
                            var eager={{(owns && !staged ? "true" : "false")}};
                            Check(probe.Created==(eager?1:0)&&probe.Calls==(eager?1:0)&&probe.Starts==0,"acquisition timing");
                            if(eager)Check(ReferenceEquals(source,probe.Last),"handler-owned route must return the handler enumerable");
                            var first=source.GetAsyncEnumerator(a.Token);var second=source.GetAsyncEnumerator(b.Token);
                            Check(!ReferenceEquals(first,second),"independent cursors");
                            a.Cancel();var canceled=first.MoveNextAsync().AsTask();
                            try{await canceled;throw new Exception("missing first cancellation");}catch(OperationCanceledException){}
                            Check(canceled.IsCanceled,"first task status");
                            Check(await second.MoveNextAsync()&&second.Current==1,"second enumeration independence");
                            if(!eager)Check(!ReferenceEquals(source,probe.Last),"orchestrated route must not return the handler enumerable");
                            api.Cancel();canceled=second.MoveNextAsync().AsTask();
                            try{await canceled;throw new Exception("missing API cancellation");}catch(OperationCanceledException){}
                            Check(canceled.IsCanceled,"API task status");
                            await first.DisposeAsync();await second.DisposeAsync();await second.DisposeAsync();
                            Check(probe.Starts==1&&probe.SourceDisposals==1&&probe.HandlerDisposals==0,"cursor cleanup and borrowed handler");
                        }
                        Check(probe.HandlerDisposals==1,"DI owns handler disposal");
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "OwnedStream_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var connection = Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalStreamConnectionModel"]
            .SelectMany(step => step.Outputs).Select(result => result.Value).OfType<CanonicalStreamConnectionCatalog>()).Routes.Single();
        Assert.Equal(owns && !staged, connection.Kind == CanonicalStreamConnectionKind.HandlerOwned);
        if (owns && !staged) Assert.Contains("not an equivalence proof", connection.Reason, StringComparison.Ordinal);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "owned-stream-" + owns + "-" + staged); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources) File.WriteAllText(Path.Combine(path, generated.HintName), generated.SourceText.ToString());
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        await ((Task)assembly.GetType("Owned.Bridge")!.GetMethod("Run")!.Invoke(null, null)!).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
