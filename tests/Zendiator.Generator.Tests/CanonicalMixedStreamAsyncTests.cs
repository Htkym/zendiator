using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string MixedStreamAsyncConfiguration = """
        c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));
        c.Handlers<Query>(DispatchFamily.SendAsync,typeof(QueryHandler));
        c.Pipeline<Query>(DispatchFamily.SendAsync);
        c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());
        """;

    private static string MixedStreamAsyncSource(bool runtime) => MixedStreamSendSource(false)
        .Replace(MixedStreamSendConfiguration, MixedStreamAsyncConfiguration)
        .Replace("namespace Mixed{", "namespace MixedAsync{")
        .Replace("ISyncRequest<int>", "IRequest<int>")
        .Replace("ISyncRequestHandler<Query,int>", "IRequestHandler<Query,int>")
        .Replace("public int SourceStarts,SourceEnds,Sends;", "public readonly global::Zendiator.Generator.Tests.AsyncOnceGate<int> Gate=new(11);public int SourceStarts,SourceEnds,Sends;")
        .Replace("public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}", "public ValueTask<int> HandleAsync(Query r,CancellationToken t){p.Sends++;return p.Gate.Task;}")
        .Replace("public int Handle(Query r,CancellationToken t)=>r.Value+1;", "public ValueTask<int> HandleAsync(Query r,CancellationToken t)=>new(r.Value+1);")
        + (runtime ? """
        namespace MixedAsync{
        public static partial class Bridge{
            public static async Task<string> Run(){
                var p=new Probe();IServiceCollection services=new ServiceCollection();
                services.AddSingleton(p);services.AddSingleton<QueryHandler>();Register(services);
                foreach(var type in new[]{typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler),
                    typeof(global::MixedAsync.Generated.Reads.IZendiator)})
                    Probe.Check(services.Count(d=>d.ServiceType==type)==1,"unique descriptor "+type);
                Probe.Check(!services.Any(d=>d.ServiceType==typeof(global::MixedAsync.Generated.Reads.Zendiator)),"interface-only mediator registration");
                Probe.Check(services.Single(d=>d.ServiceType==typeof(QueryHandler)).Lifetime==ServiceLifetime.Singleton,"TryAdd lifetime");
                var provider=services.BuildServiceProvider();
                int result;var values=new List<int>();string[] scopeEnds;
                using(var scope=provider.CreateScope()){
                    var m=scope.ServiceProvider.GetRequiredService<global::MixedAsync.Generated.Reads.IZendiator>();
                    Probe.Check(ReferenceEquals(m,scope.ServiceProvider.GetRequiredService<global::MixedAsync.Generated.Reads.IZendiator>()),"mediator identity");
                    var sequence=m.StreamAsync(new Items());
                    Probe.Check(p.SourceStarts==0&&p.Resolved.Count==0,"Stream dispatch lazy");
                    var pending=m.SendAsync(new Query(10));
                    Probe.Check(!pending.IsCompleted&&p.Gate.Consumptions==0&&p.Sends==1&&p.SourceStarts==0&&p.Resolved.SequenceEqual(new[]{"Q"}),"pending Send isolation");
                    p.Gate.Complete();result=await pending.ConfigureAwait(false);
                    Probe.Check(result==11&&p.Gate.Consumptions==1&&p.SourceStarts==0,"single Send consumption");
                    await foreach(var item in sequence)values.Add(item);
                    Probe.Check(values.SequenceEqual(new[]{2,4})&&p.Sends==1&&p.Gate.Consumptions==1&&p.SourceStarts==1&&p.SourceEnds==1,"Stream isolation/value");
                    Probe.Check(p.Ended.Count==0,"cursor borrowed cleanup");
                }
                scopeEnds=p.Ended.ToArray();
                Probe.Check(scopeEnds.Order().SequenceEqual(new[]{"F1","F2","H","M"})&&!scopeEnds.Contains("Q"),"scope ownership");
                provider.Dispose();
                Probe.Check(p.Ended.Count==5&&p.Ended.Count(n=>n=="Q")==1,"provider ownership");
                return System.Text.Json.JsonSerializer.Serialize(new{result,values,p.SourceStarts,p.SourceEnds,p.Sends,p.Gate.Consumptions,p.Resolved,scopeEnds,p.Ended});
            }
        }}
        """ : "");

    [Fact]
    public async Task Native_zero_stage_Stream_and_pending_SendAsync_keep_public_isolation()
    {
        var source = MixedStreamAsyncSource(true)
            .Replace("c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());", "c.Pipeline<Items>(DispatchFamily.Stream);")
            .Replace("typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler)", "typeof(ItemsHandler),typeof(QueryHandler)")
            .Replace("values.SequenceEqual(new[]{2,4})", "values.SequenceEqual(new[]{1,2,3})")
            .Replace("scopeEnds.Order().SequenceEqual(new[]{\"F1\",\"F2\",\"H\",\"M\"})", "scopeEnds.SequenceEqual(new[]{\"H\"})")
            .Replace("p.Ended.Count==5", "p.Ended.Count==2");
        await RunNativeStreamFixture("mixed-zero-send-async", source, "MixedAsync.Bridge");
    }

    [Fact]
    public async Task Native_staged_Stream_and_pending_SendAsync_keep_public_results_and_single_consumption()
    {
        await RunNativeStreamFixture("mixed-staged-send-async", MixedStreamAsyncSource(true), "MixedAsync.Bridge");
    }
}
