using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string StructStreamAsyncPairSource() =>
        ClosedModule(PublicValueRequestSource(false), "StructAsync", "SharedState", "Items", "H", "F1", "M", "F2", "Source") + """
        namespace Closed.StructAsync {
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using System.Text.Json;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        using Zendiator.Generator.Tests;
        public sealed record Query(int Value):IRequest<int>;
        [ZendiatorHandler]public sealed class QH:IRequestHandler<Query,int>,IDisposable {
            private readonly PublicValueRequestProbe p;private readonly AsyncOnceGate<int> gate;
            public QH(PublicValueRequestProbe p,AsyncOnceGate<int> gate){this.p=p;this.gate=gate;p.Resolved.Add("Q");}
            public ValueTask<int> HandleAsync(Query r,CancellationToken t){p.Sends++;return gate.Task;}
            public void Dispose()=>p.Ended.Add("Q");
        }
        [ZendiatorComposition("struct-async-pair")]public sealed class Reads;
        public static class Bridge {
            public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{
                c.Handlers<Items>(DispatchFamily.Stream,typeof(H));
                c.Handlers<Query>(DispatchFamily.SendAsync,typeof(QH));
                c.Pipeline<Query>(DispatchFamily.SendAsync);
                c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
            });
            private static void Check(bool value,string message)=>PublicValueRequestProbe.Check(value,message);
            public static async Task<string> Run(PublicValueRequestProbe p,AsyncOnceGate<int> gate){
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(gate);Register(services);
                using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                using var scope=provider.CreateScope();
                var mediator=scope.ServiceProvider.GetRequiredService<global::Closed.StructAsync.Generated.Reads.IZendiator>();
                var sequence=mediator.StreamAsync(new Items(new SharedState()));
                var cursor=sequence.GetAsyncEnumerator();
                Check(p.Starts==0&&p.Resolved.Count==0,"Stream dispatch/cursor lazy");
                var pending=mediator.SendAsync(new Query(10));
                Check(!pending.IsCompleted&&gate.Consumptions==0&&p.Sends==1&&p.Starts==0,"Send pending without Stream start");
                Check(p.Resolved.SequenceEqual(new[]{"Q"}),"Send only resolves its handler");
                var values=new List<int>();
                try{while(await cursor.MoveNextAsync().ConfigureAwait(false))values.Add(cursor.Current);}finally{await cursor.DisposeAsync().ConfigureAwait(false);}
                Check(values.SequenceEqual(new[]{2,4})&&p.Starts==1&&p.SourceDisposals==1,"Stream results and source lifecycle");
                Check(!pending.IsCompleted&&gate.Consumptions==0&&p.Sends==1,"Stream does not complete or repeat Send");
                Check(p.Resolved.SequenceEqual(new[]{"Q","H","F1","M","F2"})&&p.Ended.Count==0,"borrowed services remain scope owned");
                gate.Complete();var response=await pending.ConfigureAwait(false);
                Check(response==11&&gate.Consumptions==1&&p.Sends==1&&p.Starts==1,"single Send result/consumption");
                scope.Dispose();
                Check(p.Ended.SequenceEqual(new[]{"F2","M","F1","H","Q"}),"scope owns each borrowed receiver once");
                return JsonSerializer.Serialize(new{response,values,gateConsumptions=gate.Consumptions,p.Starts,p.SourceDisposals,p.Sends,p.Resolved,p.Ended});
            }
        }}
        """;

    [Fact]
    public async Task Struct_Stream_and_class_SendAsync_share_a_composition_without_pending_state_interference()
    {
        var source = StructStreamAsyncPairSource();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StructStreamAsync_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "struct-stream-async-pair");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var contract = output.GetTypeByMetadataName("Closed.StructAsync.Generated.Reads.IZendiator");
        Assert.NotNull(contract); Assert.Single(contract.GetMembers("StreamAsync")); Assert.Single(contract.GetMembers("SendAsync"));
        Assert.NotNull(output.GetTypeByMetadataName("Closed.StructAsync.Generated.Reads.ZendiatorGeneratedRegistrar"));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("Closed.StructAsync.Bridge")!.GetMethod("Run")!
            .CreateDelegate<Func<PublicValueRequestProbe, AsyncOnceGate<int>, Task<string>>>();
        var probe = new PublicValueRequestProbe(); var gate = new AsyncOnceGate<int>(11);
        string? result = null; Exception? failure = null;
        try
        {
            result = await run(probe, gate).WaitAsync(TimeSpan.FromSeconds(10));
            using var observed = JsonDocument.Parse(result);
            Assert.Equal(11, observed.RootElement.GetProperty("response").GetInt32());
            Assert.Equal(new[] { 2, 4 }, observed.RootElement.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()));
            Assert.Equal(1, gate.Consumptions); Assert.Equal(1, probe.Sends); Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.SourceDisposals);
            Assert.Equal(new[] { "F2", "M", "F1", "H", "Q" }, probe.Ended);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), JsonSerializer.Serialize(new
            {
                completed = failure == null,
                firstCause = failure?.ToString(),
                result,
                gateConsumptions = gate.Consumptions,
                probe.Sends,
                probe.Starts,
                probe.SourceDisposals,
                probe.Resolved,
                probe.Ended
            }));
        }
    }
}

