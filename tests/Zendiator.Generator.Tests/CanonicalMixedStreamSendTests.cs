using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string MixedStreamSendConfiguration = """
        c.Handlers<Items>(DispatchFamily.Stream,typeof(ItemsHandler));
        c.Pipeline<Items>(DispatchFamily.Stream,Stage.Hook<Positive>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>());
        c.Handlers<Query>(DispatchFamily.SendSync,typeof(QueryHandler));
        c.Pipeline<Query>(DispatchFamily.SendSync);
        """;
    private static string MixedStreamSendSource(bool runtime) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace Mixed{
        public sealed record Items:IStreamRequest<int>;
        public sealed record Query(int Value):ISyncRequest<int>;
        public sealed class Probe{
            public int SourceStarts,SourceEnds,Sends;public List<string> Resolved=new();public List<string> Ended=new();
            public void Resolve(string name)=>Resolved.Add(name);
            public void End(string name)=>Ended.Add(name);
            public static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        }
        [ZendiatorHandler]public sealed class ItemsHandler:IStreamSourceHandler<Items,int,ItemsHandler.Cursor>,IDisposable{
            private readonly Probe p;public ItemsHandler(Probe p){this.p=p;p.Resolve("H");}
            public ValueTask<Cursor> OpenAsync(Items r,CancellationToken t){p.SourceStarts++;return new(new Cursor(p,t));}
            public sealed class Cursor(Probe p,CancellationToken token):IAsyncEnumerator<int>{
                private int value;public int Current=>value;
                public ValueTask<bool> MoveNextAsync(){token.ThrowIfCancellationRequested();return new(++value<=3);}
                public ValueTask DisposeAsync(){p.SourceEnds++;return default;}
            }
            public void Dispose()=>p.End("H");
        }
        public sealed class Positive:IStreamItemFilter<Items,int>,IDisposable{
            private readonly Probe p;public Positive(Probe p){this.p=p;p.Resolve("F1");}
            public bool Accept(Items r,int item,CancellationToken t)=>item>0;
            public void Dispose()=>p.End("F1");
        }
        public sealed class DoubleItem:IStreamItemMap<Items,int>,IDisposable{
            private readonly Probe p;public DoubleItem(Probe p){this.p=p;p.Resolve("M");}
            public int Map(Items r,int item,CancellationToken t)=>item*2;
            public void Dispose()=>p.End("M");
        }
        public sealed class Small:IStreamItemFilter<Items,int>,IDisposable{
            private readonly Probe p;public Small(Probe p){this.p=p;p.Resolve("F2");}
            public bool Accept(Items r,int item,CancellationToken t)=>item<6;
            public void Dispose()=>p.End("F2");
        }
        [ZendiatorHandler]public sealed class QueryHandler:ISyncRequestHandler<Query,int>,IDisposable{
            private readonly Probe p;public QueryHandler(Probe p){this.p=p;p.Resolve("Q");}
            public int Handle(Query r,CancellationToken t){p.Sends++;return r.Value+1;}
            public void Dispose()=>p.End("Q");
        }
        [ZendiatorHandler]public sealed class ReplacementQueryHandler:ISyncRequestHandler<Query,int>{
            public int Handle(Query r,CancellationToken t)=>r.Value+1;
        }
        [ZendiatorComposition("mixed-read")]public sealed class Reads;
        public static partial class Bridge{
            public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{__CONFIG__});
        }
        }
        """.Replace("__CONFIG__", MixedStreamSendConfiguration) + (runtime ? """
        namespace Mixed{
        public static partial class Bridge{
            public static async Task<string> Run(){
                var p=new Probe();IServiceCollection services=new ServiceCollection();
                services.AddSingleton(p);services.AddSingleton<QueryHandler>();Register(services);
                foreach(var type in new[]{typeof(ItemsHandler),typeof(Positive),typeof(DoubleItem),typeof(Small),typeof(QueryHandler),
                    typeof(global::Mixed.Generated.Reads.IZendiator)})
                    Probe.Check(services.Count(d=>d.ServiceType==type)==1,"unique descriptor "+type);
                Probe.Check(!services.Any(d=>d.ServiceType==typeof(global::Mixed.Generated.Reads.Zendiator)),"interface-only mediator registration");
                Probe.Check(services.Single(d=>d.ServiceType==typeof(QueryHandler)).Lifetime==ServiceLifetime.Singleton,"TryAdd lifetime");
                var provider=services.BuildServiceProvider();
                int result;var values=new List<int>();string[] scopeEnds;
                using(var scope=provider.CreateScope()){
                    var m=scope.ServiceProvider.GetRequiredService<global::Mixed.Generated.Reads.IZendiator>();
                    Probe.Check(ReferenceEquals(m,scope.ServiceProvider.GetRequiredService<global::Mixed.Generated.Reads.IZendiator>()),"mediator identity");
                    var sequence=m.StreamAsync(new Items());
                    Probe.Check(p.SourceStarts==0&&p.Resolved.Count==0,"Stream dispatch lazy");
                    result=m.Send(new Query(10));
                    Probe.Check(result==11&&p.Sends==1&&p.SourceStarts==0&&p.Resolved.SequenceEqual(new[]{"Q"}),"Send isolation");
                    await foreach(var item in sequence)values.Add(item);
                    Probe.Check(values.SequenceEqual(new[]{2,4})&&p.Sends==1&&p.SourceStarts==1&&p.SourceEnds==1,"Stream isolation/value");
                    Probe.Check(p.Ended.Count==0,"cursor borrowed cleanup");
                }
                scopeEnds=p.Ended.ToArray();
                Probe.Check(scopeEnds.Order().SequenceEqual(new[]{"F1","F2","H","M"})&&!scopeEnds.Contains("Q"),"scope ownership");
                provider.Dispose();
                Probe.Check(p.Ended.Count==5&&p.Ended.Count(n=>n=="Q")==1,"provider ownership");
                return System.Text.Json.JsonSerializer.Serialize(new{result,values,p.SourceStarts,p.SourceEnds,p.Sends,p.Resolved,scopeEnds,p.Ended});
            }
        }}
        """ : "");

    [Fact]
    public async Task Native_Stream_and_SendSync_keep_public_results_DI_ownership_and_route_identity_local()
    {
        var source = MixedStreamSendSource(true);
        await RunNativeStreamFixture("mixed-send-sync", source, "Mixed.Bridge");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "NativeMixedIdentity"), out _, out var diagnostics);
        Assert.Empty(diagnostics);
        var initial = driver.GetRunResult().Results.Single().GeneratedSources;
        var stream = CanonicalRouteSource(initial, "Mixed.Items");
        var send = CanonicalRouteSource(initial, "Mixed.Query");
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(source.Replace("typeof(QueryHandler)", "typeof(ReplacementQueryHandler)"), "NativeMixedIdentity"), out var output, out diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var updated = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Equal(stream, CanonicalRouteSource(updated, "Mixed.Items"));
        Assert.NotEqual(send, CanonicalRouteSource(updated, "Mixed.Query"));
        Assert.Contains("ReplacementQueryHandler", CanonicalCompositionSource(updated));
    }
}
