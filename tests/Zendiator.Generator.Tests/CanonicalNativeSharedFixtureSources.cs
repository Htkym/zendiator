using Zendiator.Tests.Contracts;

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
    private static string TypeChangeSource(string _) => ListFixture("""
        c.Handlers<ChangeRequest>(DispatchFamily.Stream,typeof(ChangeHandler));
        c.StreamPipeline<ChangeRequest,long>(Stage.Hook(typeof(ToReference<>)),Stage.Hook<ToFinal>());
        """) + """
        public sealed record ChangeRequest:IStreamRequest<int>;
        [ZendiatorHandler] public sealed class ChangeHandler(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p):IStreamSourceHandler<ChangeRequest,int,ChangeHandler.Cursor>{
            public ValueTask<Cursor> OpenAsync(ChangeRequest r,CancellationToken t)=>new(new Cursor(p.Start(r,t).GetAsyncEnumerator(t)));
            public sealed class Cursor(System.Collections.Generic.IAsyncEnumerator<int> inner):System.Collections.Generic.IAsyncEnumerator<int>{
                public int Current=>inner.Current;public ValueTask<bool> MoveNextAsync()=>inner.MoveNextAsync();public ValueTask DisposeAsync()=>inner.DisposeAsync();
            }
        }
        public sealed class ToReference<T>(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p):IStreamItemMap<ChangeRequest,T,string?> where T:struct{
            public string? Map(ChangeRequest r,T item,CancellationToken t)=>p.FirstText(r,item.ToString(),t);
        }
        public sealed class ToFinal(global::Zendiator.Generator.Tests.CanonicalStreamTypeChangeProbe p):IAsyncStreamItemMap<ChangeRequest,string?,long>{
            public ValueTask<long> MapAsync(ChangeRequest r,string? item,CancellationToken t)=>p.Last(r,item,t);
        }
        """;

    private static string PublicValueRequestSource(bool _) => """
        using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
        using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Zendiator.Generator.Tests;
        namespace ValuePublic{
        public sealed class SharedState{public int Version;}
        public struct Items:IStreamRequest<int>{
            public int Marker;public long Pad;public SharedState? Shared;
            public Items(SharedState shared){Marker=7;Pad=11;Shared=shared;}
        }
        [ZendiatorHandler]public sealed class H:IStreamSourceHandler<Items,int,Source.Cursor>,IDisposable{
            private readonly PublicValueRequestProbe p;public H(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("H");}
            public ValueTask<Source.Cursor> OpenAsync(Items r,CancellationToken t){
                p.Starts++;p.Record("H",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);
                r.Marker=101;r.Pad=1001;r.Shared!.Version++;return new(new Source.Cursor(p));
            }
            public void Dispose()=>p.Ended.Add("H");
        }
        public sealed class F1:IStreamItemFilter<Items,int>,IDisposable{
            private readonly PublicValueRequestProbe p;public F1(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("F1");}
            public bool Accept(Items r,int item,CancellationToken t){p.Record("F1",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=102;r.Pad=1002;return item>0;}
            public void Dispose()=>p.Ended.Add("F1");
        }
        public sealed class M:IStreamItemMap<Items,int>,IDisposable{
            private readonly PublicValueRequestProbe p;public M(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("M");}
            public int Map(Items r,int item,CancellationToken t){p.Record("M",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=103;r.Pad=1003;return item*2;}
            public void Dispose()=>p.Ended.Add("M");
        }
        public sealed class F2:IStreamItemFilter<Items,int>,IDisposable{
            private readonly PublicValueRequestProbe p;public F2(PublicValueRequestProbe p){this.p=p;p.Resolved.Add("F2");}
            public bool Accept(Items r,int item,CancellationToken t){p.Record("F2",r.Marker,r.Pad,r.Shared,r.Shared?.Version??-1);r.Marker=104;r.Pad=1004;return item<6;}
            public void Dispose()=>p.Ended.Add("F2");
        }
        public sealed class Source{
            public sealed class Cursor(PublicValueRequestProbe p):IAsyncEnumerator<int>{
                private int value;public int Current=>value;public ValueTask<bool> MoveNextAsync()=>new(++value<=3);
                public ValueTask DisposeAsync(){p.SourceDisposals++;return default;}
            }
        }
        }
        """;
}
