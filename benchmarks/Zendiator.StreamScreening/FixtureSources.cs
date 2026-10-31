using System.Text.RegularExpressions;

namespace Zendiator.StreamScreening;

internal static class FixtureSources
{
    internal static readonly string[] Shapes = ["completed", "suspended", "open-session", "large-middleware-session", "zero-hook-sentinel"];
    internal static long Expected(string shape) => shape switch
    {
        "open-session" => 7,
        "large-middleware-session" => 701,
        "zero-hook-sentinel" => 1,
        _ => 2
    };

    // One template per shape; only /*C:n*/ instrumentation and gate type differ
    // between the preflight and measured copies. No counter-mode branch exists
    // in the compiled measurement callbacks.
    internal static string Create(string shape)
    {
        var template = ExpandFactories(Common + (shape switch
        {
            "completed" or "suspended" => Async,
            "open-session" => Open,
            "large-middleware-session" => Large,
            "zero-hook-sentinel" => Zero,
            _ => throw new ArgumentException("Unknown fixed shape.")
        }));
        string Copy(string mode, bool count)
        {
            var text = template.Replace("@NS@", "App." + mode).Replace("@MODE@", mode)
                .Replace("@GATE@", count ? "CountedCompletionGate" : "CompletionGate");
            return Regex.Replace(text, @"/\*C:(\d+)\*/", m =>
            {
                if (!count)
                    return "_ = p;";
                var slot = int.Parse(m.Groups[1].Value);
                var verify = slot is 0 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 24 ? "p.Verify(r!,c);" : slot == 1 ? "p.VerifyToken(token);" : "";
                return verify + "p.Counts[" + slot + "]++;";
            });
        }
        return Copy("Preflight", true) + Copy("Measure", false);
    }

    internal static string Bridge(string shape)
    {
        var item = shape switch
        {
            "open-session" => "Final<Payload>",
            "large-middleware-session" => "LargeItem",
            _ => "int"
        };
        var request = shape == "open-session" ? "Request<Payload>" : "Request";
        var value = shape switch
        {
            "open-session" => "item.Value.Value",
            "large-middleware-session" => "item.Value",
            _ => "item"
        };
        string Copy(string mode) => $$"""
            namespace App.{{mode}} {
                public static partial class Consumer {
                    public static IAsyncEnumerable<{{item}}> Candidate(IServiceProvider s, {{request}} r) => s.GetRequiredService<global::App.{{mode}}.Generated.Composition.IZendiator>().StreamAsync(r,default);
                    public static async Task<Outcome> Batch(IServiceProvider provider, Context p, {{request}} request, bool reference, int count) {
                        long checksum=0, outputs=0;
                        for(var i=0;i<count;i++) {
                            await using(var scope=provider.CreateAsyncScope()) {
                                var stream=reference ? Oracle(scope.ServiceProvider,request) : Candidate(scope.ServiceProvider,request);
                                var cursor=stream.GetAsyncEnumerator();
                                try { while(await cursor.MoveNextAsync().ConfigureAwait(false)) { var item=cursor.Current; {{(mode == "Preflight" && shape == "open-session" ? "if(!ReferenceEquals(item.Value,request.Value)) throw new InvalidOperationException(\"Payload identity.\");" : "")}} checksum+={{value}}; outputs++; } }
                                finally { await cursor.DisposeAsync().ConfigureAwait(false); }
                            }
                            p.Drain();
                        }
                        return new(checksum,outputs);
                    }
                }
            }
            """;
        return Copy("Preflight") + Copy("Measure");
    }

    internal static string Entry(string shape) => $$"""
        public static class Entry {
            public static async Task<string> Run(int launch) {
                var pending={{(shape == "suspended" ? "true" : "false")}};
                object Preflight(bool reference) {
                    using var p=new App.Preflight.Context(pending);
                    using var provider=App.Preflight.Setup.Provider(p);
                    var request=App.Preflight.Setup.Request();
                    p.ExpectedRequest=request;
                    var output=App.Preflight.Consumer.Batch(provider,p,request,reference,1).GetAwaiter().GetResult();
                    p.Drain();
                    App.Preflight.Setup.Check(p,output);
                    return new { p.Counts, output, firstGetResult=p.First.Consumptions, mapGetResult=p.Map.Consumptions,
                        pendingRegistrations=p.First.Registrations, workerDrained=true };
                }
                var proofA=Preflight(true); var proofB=Preflight(false);
                if(JsonSerializer.Serialize(proofA)!=JsonSerializer.Serialize(proofB)) throw new InvalidOperationException("Preflight counts differ.");
                using var a=new App.Measure.Context(pending); using var b=new App.Measure.Context(pending);
                using var ap=App.Measure.Setup.Provider(a); using var bp=App.Measure.Setup.Provider(b);
                var ar=App.Measure.Setup.Request(); var br=App.Measure.Setup.Request();
                var json=await ScreeningRun.Run("{{shape}}",launch,{{Expected(shape)}},
                    n=>App.Measure.Consumer.Batch(ap,a,ar,true,n), n=>App.Measure.Consumer.Batch(bp,b,br,false,n),
                    a.Drain,b.Drain,new { A=proofA,B=proofB }).ConfigureAwait(false);
                return json; // using scopes join both workers before host observes completion.
            }
        }
        """;

    // Counts for the new protocol: handler Open/cursor acquisition/Move/Current/Dispose (0..4), F1/Map/F2 (5..7),
    // owned Create H/A-or-M/W/B (8..11), owned Dispose (12..15),
    // factory Dispose (16..19), scoped DI constructors (20..23), middleware Open (24).
    private const string Common = """
        namespace @NS@ {
            public sealed class Context(bool pending):IDisposable {
                public readonly int[] Counts=new int[25];
                public object? ExpectedRequest;
                public readonly @GATE@<bool> First=new(pending);
                public readonly @GATE@<int> Map=new(false);
                public bool Pending {get;}=pending;
                public void Verify(object request,CancellationToken token) {
                    if(!ReferenceEquals(ExpectedRequest,request)) throw new InvalidOperationException("Request identity.");
                    VerifyToken(token);
                }
                public void VerifyToken(CancellationToken token) {
                    if(token!=default) throw new InvalidOperationException("Token identity.");
                }
                public void Drain() { First.WaitIdle();Map.WaitIdle(); }
                public void Dispose() { First.Dispose();Map.Dispose(); }
            }
            public sealed class Source<T>:IAsyncEnumerator<T> {
                private readonly Context p;
                private readonly T item;
                private int index;
                public Source(Context context,T value,CancellationToken token) { p=context; item=value; /*C:1*/ }
                public T Current {get { /*C:3*/ return item; }}
                public ValueTask<bool> MoveNextAsync() { /*C:2*/ return new(index++==0); }
                public ValueTask DisposeAsync() { /*C:4*/ return default; }
            }
        }
        """;

    private const string Async = """
        namespace @NS@ {
            [ZendiatorComposition("screen-@MODE@")] public sealed class Composition;
            public static class Declaration {
                public static void Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=> {
                    c.Handlers<Request>(DispatchFamily.Stream,typeof(H));
                    c.Pipeline<Request>(DispatchFamily.Stream,Stage.Hook<F1>(),Stage.Hook<M>(),Stage.Hook<F2>());
                });
            }
            public sealed record Request:IStreamRequest<int>;
            [ZendiatorHandler] public sealed class H:IStreamSourceHandler<Request,int,Source<int>> {
                private readonly Context p;
                public H(Context context) {p=context; /*C:20*/}
                public ValueTask<Source<int>> OpenAsync(Request r,CancellationToken c) { /*C:0*/ return new(new Source<int>(p,1,c)); }
            }
            public sealed class F1:IAsyncStreamItemFilter<Request,int> {
                private readonly Context p;
                public F1(Context context) {p=context; /*C:21*/}
                public ValueTask<bool> AcceptAsync(Request r,int item,CancellationToken c) { /*C:5*/ return p.First.Begin(true); }
            }
            public sealed class M:IAsyncStreamItemMap<Request,int> {
                private readonly Context p;
                public M(Context context) {p=context; /*C:22*/}
                public ValueTask<int> MapAsync(Request r,int item,CancellationToken c) { /*C:6*/ return p.Map.Begin(item*2); }
            }
            public sealed class F2:IStreamItemFilter<Request,int> {
                private readonly Context p;
                public F2(Context context) {p=context; /*C:23*/}
                public bool Accept(Request r,int item,CancellationToken c) { /*C:7*/ return item<4; }
            }
            public static partial class Consumer {
                public static async IAsyncEnumerable<int> Oracle(IServiceProvider s,Request r) {
                    await using var cursor=await s.GetRequiredService<H>().OpenAsync(r,default).ConfigureAwait(false);
                    F1? f1=null; M? map=null; F2? f2=null;
                    while(await cursor.MoveNextAsync().ConfigureAwait(false)) {
                        var item=cursor.Current;
                        f1 ??= s.GetRequiredService<F1>();
                        if(!await f1.AcceptAsync(r,item,default).ConfigureAwait(false)) continue;
                        map ??= s.GetRequiredService<M>();
                        var mapped=await map.MapAsync(r,item,default).ConfigureAwait(false);
                        f2 ??= s.GetRequiredService<F2>();
                        if(f2.Accept(r,mapped,default)) yield return mapped;
                    }
                }
            }
            public static class Setup {
                public static Request Request()=>new();
                public static ServiceProvider Provider(Context p) { var services=new ServiceCollection().AddSingleton(p).AddScoped<H>().AddScoped<F1>().AddScoped<M>().AddScoped<F2>(); Declaration.Register(services); return services.BuildServiceProvider(); }
                public static void Check(Context p,Outcome output) {
                    if(output!=new Outcome(2,1)|| !p.Counts.SequenceEqual(new[]{1,1,2,1,1,1,1,1,0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,0})) throw new InvalidOperationException("Async contract counts.");
                    if(((CountedCompletionGate<bool>)(object)p.First).Consumptions!=1||((CountedCompletionGate<int>)(object)p.Map).Consumptions!=1||((CountedCompletionGate<bool>)(object)p.First).Registrations!=(p.Pending?1:0)) throw new InvalidOperationException("Single consumption/real suspension.");
                }
            }
        }
        """;

    private const string Open = """
        namespace @NS@ {
            [ZendiatorComposition("screen-@MODE@")] public sealed class Composition;
            public static class Declaration {
                public static void Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=> {
                    c.Handlers(typeof(Request<>),DispatchFamily.Stream,HandlerEntry.Session(typeof(H<>),typeof(HF<>)));
                    c.StreamPipeline(typeof(Request<>),typeof(Final<>),Stage.Hook(typeof(M<,>),typeof(MF<,>)));
                });
            }
            public sealed class Payload {public long Value {get;}=7;}
            public sealed record Request<T>(T Value):IStreamRequest<T>;
            public sealed record Final<T>(T Value);
            [ZendiatorHandler] public sealed class H<T>(Context p):IStreamSourceHandler<Request<T>,T,Source<T>>,IAsyncDisposable where T:class,new() {
                public ValueTask<Source<T>> OpenAsync(Request<T> r,CancellationToken c) { /*C:0*/ return new(new Source<T>(p,r.Value,c)); }
                public ValueTask DisposeAsync() { /*C:12*/ return default; }
            }
            public sealed class M<R,T>(Context p):IStreamItemMap<R,T,Final<T>>,IAsyncDisposable {
                public Final<T> Map(R r,T item,CancellationToken c) { /*C:6*/ return new(item); }
                public ValueTask DisposeAsync() { /*C:13*/ return default; }
            }
            public sealed class HF<T>:IOwnedSessionFactory<Request<T>,H<T>>,IDisposable where T:class,new() {
                private readonly Context p;
                public HF(Context context) {p=context; /*C:20*/}
                public H<T> Create(Request<T> r,CancellationToken c) { /*C:8*/ return new(p); }
                public void Dispose() { /*C:16*/ }
            }
            public sealed class MF<R,T>:IOwnedSessionFactory<R,M<R,T>>,IDisposable {
                private readonly Context p;
                public MF(Context context) {p=context; /*C:21*/}
                public M<R,T> Create(R r,CancellationToken c) { /*C:9*/ return new(p); }
                public void Dispose() { /*C:17*/ }
            }
            public static partial class Consumer {
                public static async IAsyncEnumerable<Final<Payload>> Oracle(IServiceProvider s,Request<Payload> r) {
                    var handler=s.GetRequiredService<HF<Payload>>().Create(r,default);
                    Source<Payload>? cursor=null; M<Request<Payload>,Payload>? map=null;
                    try {
                        cursor=await handler.OpenAsync(r,default).ConfigureAwait(false);
                        while(await cursor.MoveNextAsync().ConfigureAwait(false)) {
                            var item=cursor.Current;
                            map ??= s.GetRequiredService<MF<Request<Payload>,Payload>>().Create(r,default);
                            yield return map.Map(r,item,default);
                        }
                    }
                    finally {
                        try { if(cursor is not null) await cursor.DisposeAsync().ConfigureAwait(false); }
                        finally {
                            try { if(map is not null) await map.DisposeAsync().ConfigureAwait(false); }
                            finally { await handler.DisposeAsync().ConfigureAwait(false); }
                        }
                    }
                }
            }
            public static class Setup {
                public static Request<Payload> Request()=>new(new Payload());
                public static ServiceProvider Provider(Context p) { var services=new ServiceCollection().AddSingleton(p).AddScoped(typeof(HF<>)).AddScoped(typeof(MF<,>)); Declaration.Register(services); return services.BuildServiceProvider(); }
                public static void Check(Context p,Outcome output) {
                    if(output!=new Outcome(7,1)|| !p.Counts.SequenceEqual(new[]{1,1,2,1,1,0,1,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0,0,0})) throw new InvalidOperationException("Open session contract counts.");
                }
            }
        }
        """;

    private const string Large = """
        namespace @NS@ {
            [ZendiatorComposition("screen-@MODE@")] public sealed class Composition;
            public static class Declaration {
                public static void Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=> {
                    c.Handlers<Request>(DispatchFamily.Stream,HandlerEntry.Session<H,HF>());
                    c.StreamPipeline<Request,LargeItem>(Stage.Hook<A,AF>(),Stage.Wrapper<W,WF>(),Stage.Hook<B,BF>());
                });
            }
            public sealed record Request:IStreamRequest<int>;
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,Size=128)]
            public readonly struct LargeItem(long value) {public long Value {get;}=value;}
            [ZendiatorHandler] public sealed class H(Context p):IStreamSourceHandler<Request,int,Source<int>>,IAsyncDisposable {
                public ValueTask<Source<int>> OpenAsync(Request r,CancellationToken c) { /*C:0*/ return new(new Source<int>(p,7,c)); }
                public ValueTask DisposeAsync() { /*C:12*/ return default; }
            }
            public sealed class A(Context p):IStreamItemMap<Request,int,LargeItem>,IAsyncDisposable {
                public LargeItem Map(Request r,int item,CancellationToken c) { /*C:6*/ return new(item*100L); }
                public ValueTask DisposeAsync() { /*C:13*/ return default; }
            }
            public sealed class W(Context p):IStreamMiddleware<Request,LargeItem>,IAsyncDisposable {
                public ValueTask<IAsyncEnumerator<LargeItem>> OpenAsync(Request r,IStreamUpstream<Request,LargeItem> next,CancellationToken c) { /*C:24*/ return next.OpenAsync(r,c); }
                public ValueTask DisposeAsync() { /*C:14*/ return default; }
            }
            public sealed class B(Context p):IAsyncStreamItemMap<Request,LargeItem,LargeItem>,IAsyncDisposable {
                public ValueTask<LargeItem> MapAsync(Request r,LargeItem item,CancellationToken c) { /*C:7*/ return new(new LargeItem(item.Value+1)); }
                public ValueTask DisposeAsync() { /*C:15*/ return default; }
            }
            @FACTORIES@
            public static partial class Consumer {
                public static async IAsyncEnumerable<LargeItem> Oracle(IServiceProvider s,Request r) {
                    var handler=s.GetRequiredService<HF>().Create(r,default);
                    Source<int>? source=null; A? before=null; W? middleware=null; B? after=null;
                    IAsyncEnumerator<LargeItem>? cursor=null;
                    try {
                        source=await handler.OpenAsync(r,default).ConfigureAwait(false);
                        before=s.GetRequiredService<AF>().Create(r,default);
                        middleware=s.GetRequiredService<WF>().Create(r,default);
                        cursor=await middleware.OpenAsync(r,new Before(source,before,r),default).ConfigureAwait(false);
                        source=null; // The returned cursor now owns source disposal.
                        while(await cursor.MoveNextAsync().ConfigureAwait(false)) {
                            var item=cursor.Current;
                            after ??= s.GetRequiredService<BF>().Create(r,default);
                            yield return await after.MapAsync(r,item,default).ConfigureAwait(false);
                        }
                    }
                    finally {
                        try { if(cursor is not null) await cursor.DisposeAsync().ConfigureAwait(false); else if(source is not null) await source.DisposeAsync().ConfigureAwait(false); }
                        finally {
                            try { if(after is not null) await after.DisposeAsync().ConfigureAwait(false); }
                            finally {
                                try { if(middleware is not null) await middleware.DisposeAsync().ConfigureAwait(false); }
                                finally { try { if(before is not null) await before.DisposeAsync().ConfigureAwait(false); } finally { await handler.DisposeAsync().ConfigureAwait(false); } }
                            }
                        }
                    }
                }
                private sealed class Before(Source<int> source,A map,Request request):IStreamUpstream<Request,LargeItem>,IAsyncEnumerator<LargeItem> {
                    public LargeItem Current {get;private set;}
                    public ValueTask<IAsyncEnumerator<LargeItem>> OpenAsync(Request r,CancellationToken c)=>new(this);
                    public async ValueTask<bool> MoveNextAsync() {
                        if(!await source.MoveNextAsync().ConfigureAwait(false)) return false;
                        Current=map.Map(request,source.Current,default); return true;
                    }
                    public ValueTask DisposeAsync()=>source.DisposeAsync();
                }
            }
            public static class Setup {
                public static Request Request()=>new();
                public static ServiceProvider Provider(Context p) { var services=new ServiceCollection().AddSingleton(p).AddScoped<HF>().AddScoped<AF>().AddScoped<WF>().AddScoped<BF>(); Declaration.Register(services); return services.BuildServiceProvider(); }
                public static void Check(Context p,Outcome output) {
                    if(output!=new Outcome(701,1)||System.Runtime.InteropServices.Marshal.SizeOf<LargeItem>()!=128|| !p.Counts.SequenceEqual(new[]{1,1,2,1,1,0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1})) throw new InvalidOperationException("Large middleware session contract counts.");
                }
            }
        }
        """;

    private const string Zero = """
        namespace @NS@ {
            [ZendiatorComposition("screen-@MODE@")] public sealed class Composition;
            public static class Declaration {
                public static void Register(IServiceCollection services)=>services.AddZendiator<Composition>(c=> {
                    c.Handlers<Request>(DispatchFamily.Stream,typeof(H));
                    c.Pipeline<Request>(DispatchFamily.Stream);
                });
            }
            public sealed record Request:IStreamRequest<int>;
            [ZendiatorHandler] public sealed class H:IStreamSourceHandler<Request,int,Source<int>> {
                private readonly Context p;
                public H(Context context) {p=context; /*C:20*/}
                public ValueTask<Source<int>> OpenAsync(Request r,CancellationToken c) { /*C:0*/ return new(new Source<int>(p,1,c)); }
            }
            public static partial class Consumer {
                public static async IAsyncEnumerable<int> Oracle(IServiceProvider s,Request r) {
                    await using var cursor=await s.GetRequiredService<H>().OpenAsync(r,default).ConfigureAwait(false);
                    while(await cursor.MoveNextAsync().ConfigureAwait(false)) yield return cursor.Current;
                }
            }
            public static class Setup {
                public static Request Request()=>new();
                public static ServiceProvider Provider(Context p) { var services=new ServiceCollection().AddSingleton(p).AddScoped<H>(); Declaration.Register(services); return services.BuildServiceProvider(); }
                public static void Check(Context p,Outcome output) {
                    if(output!=new Outcome(1,1)|| !p.Counts.SequenceEqual(new[]{1,1,2,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0})) throw new InvalidOperationException("Zero hook counts.");
                }
            }
        }
        """;

    internal static string ExpandFactories(string source)
    {
        var roles = new[] { "H", "A", "W", "B" };
        return source.Replace("@FACTORIES@", string.Concat(roles.Select((role, i) => $$"""
            public sealed class {{role}}F:IOwnedSessionFactory<Request,{{role}}>,IDisposable {
                private readonly Context p;
                public {{role}}F(Context context) {p=context; /*C:{{20 + i}}*/}
                public {{role}} Create(Request r,CancellationToken c) { /*C:{{8 + i}}*/ return new(p); }
                public void Dispose() { /*C:{{16 + i}}*/ }
            }
            """)));
    }
}
