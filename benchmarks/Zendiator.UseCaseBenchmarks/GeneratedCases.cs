#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zendiator.DependencyInjection;

using Zendiator.CompetitiveBenchmarks;
using DispatchR.Extensions;
using BenchmarkDotNet.Attributes;
using Z = Zendiator;
using M = MediatR;
using G = Mediator;
using D = DispatchR.Abstractions.Send;
using DS = DispatchR.Abstractions.Stream;
using I = Immediate.Handlers.Shared;
[assembly: I.ImmediateAssemblyIdentifier("Competitive")]
namespace Competitive;
public sealed record Ping0(int Value = 41, Probe? Probe = null) : IWork, Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<Ping0, ValueTask<int>>;
public sealed record Stream0(int Count, bool Asynchronous, Probe? Probe = null) : IWork, Z.IStreamRequest<int>, M.IStreamRequest<int>, G.IStreamRequest<int>, DS.IStreamRequest<Stream0, int>;
public sealed class ZPing0 : Z.IRequestHandler<Ping0,int> { public ValueTask<int> HandleAsync(Ping0 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MPing0 : M.IRequestHandler<Ping0,int> { public Task<int> Handle(Ping0 r,CancellationToken t) => Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class GPing0 : G.IRequestHandler<Ping0,int> { public ValueTask<int> Handle(Ping0 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DPing0 : D.IRequestHandler<Ping0,ValueTask<int>> { public ValueTask<int> Handle(Ping0 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZStream0 : Z.IStreamRequestHandler<Stream0,int> { public IAsyncEnumerable<int> HandleAsync(Stream0 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class MStream0 : M.IStreamRequestHandler<Stream0,int> { public IAsyncEnumerable<int> Handle(Stream0 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class GStream0 : G.IStreamRequestHandler<Stream0,int> { public IAsyncEnumerable<int> Handle(Stream0 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DStream0 : DS.IStreamRequestHandler<Stream0,int> { public IAsyncEnumerable<int> Handle(Stream0 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
[I.Handler] [I.Behaviors()]
public static partial class IPing0 { private static ValueTask<int> HandleAsync(Ping0 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler] [I.Behaviors()]
public static partial class IStream0 { private static IAsyncEnumerable<int> HandleAsync(Stream0 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed record Ping1(int Value = 41, Probe? Probe = null) : ILevel1, Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<Ping1, ValueTask<int>>;
public sealed record Stream1(int Count, bool Asynchronous, Probe? Probe = null) : ILevel1, Z.IStreamRequest<int>, M.IStreamRequest<int>, G.IStreamRequest<int>, DS.IStreamRequest<Stream1, int>;
public sealed class ZPing1 : Z.IRequestHandler<Ping1,int> { public ValueTask<int> HandleAsync(Ping1 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MPing1 : M.IRequestHandler<Ping1,int> { public Task<int> Handle(Ping1 r,CancellationToken t) => Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class GPing1 : G.IRequestHandler<Ping1,int> { public ValueTask<int> Handle(Ping1 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DPing1 : D.IRequestHandler<Ping1,ValueTask<int>> { public ValueTask<int> Handle(Ping1 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZStream1 : Z.IStreamRequestHandler<Stream1,int> { public IAsyncEnumerable<int> HandleAsync(Stream1 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class MStream1 : M.IStreamRequestHandler<Stream1,int> { public IAsyncEnumerable<int> Handle(Stream1 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class GStream1 : G.IStreamRequestHandler<Stream1,int> { public IAsyncEnumerable<int> Handle(Stream1 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DStream1 : DS.IStreamRequestHandler<Stream1,int> { public IAsyncEnumerable<int> Handle(Stream1 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DBehavior1_1 : D.IPipelineBehavior<Ping1,ValueTask<int>>
{ public required D.IRequestHandler<Ping1,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping1 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior1_1 : DS.IStreamPipelineBehavior<Stream1,int>
{ public required DS.IStreamRequestHandler<Stream1,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream1 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>))]
public static partial class IPing1 { private static ValueTask<int> HandleAsync(Ping1 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler] [I.Behaviors(typeof(ISBehavior1<,>))]
public static partial class IStream1 { private static IAsyncEnumerable<int> HandleAsync(Stream1 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed record Ping3(int Value = 41, Probe? Probe = null) : ILevel3, Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<Ping3, ValueTask<int>>;
public sealed record Stream3(int Count, bool Asynchronous, Probe? Probe = null) : ILevel3, Z.IStreamRequest<int>, M.IStreamRequest<int>, G.IStreamRequest<int>, DS.IStreamRequest<Stream3, int>;
public sealed class ZPing3 : Z.IRequestHandler<Ping3,int> { public ValueTask<int> HandleAsync(Ping3 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MPing3 : M.IRequestHandler<Ping3,int> { public Task<int> Handle(Ping3 r,CancellationToken t) => Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class GPing3 : G.IRequestHandler<Ping3,int> { public ValueTask<int> Handle(Ping3 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DPing3 : D.IRequestHandler<Ping3,ValueTask<int>> { public ValueTask<int> Handle(Ping3 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZStream3 : Z.IStreamRequestHandler<Stream3,int> { public IAsyncEnumerable<int> HandleAsync(Stream3 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class MStream3 : M.IStreamRequestHandler<Stream3,int> { public IAsyncEnumerable<int> Handle(Stream3 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class GStream3 : G.IStreamRequestHandler<Stream3,int> { public IAsyncEnumerable<int> Handle(Stream3 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DStream3 : DS.IStreamRequestHandler<Stream3,int> { public IAsyncEnumerable<int> Handle(Stream3 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DBehavior3_1 : D.IPipelineBehavior<Ping3,ValueTask<int>>
{ public required D.IRequestHandler<Ping3,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping3 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior3_1 : DS.IStreamPipelineBehavior<Stream3,int>
{ public required DS.IStreamRequestHandler<Stream3,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream3 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior3_2 : D.IPipelineBehavior<Ping3,ValueTask<int>>
{ public required D.IRequestHandler<Ping3,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping3 r,CancellationToken t) { Work.Enter(r,2); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior3_2 : DS.IStreamPipelineBehavior<Stream3,int>
{ public required DS.IStreamRequestHandler<Stream3,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream3 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,2); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior3_3 : D.IPipelineBehavior<Ping3,ValueTask<int>>
{ public required D.IRequestHandler<Ping3,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping3 r,CancellationToken t) { Work.Enter(r,3); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior3_3 : DS.IStreamPipelineBehavior<Stream3,int>
{ public required DS.IStreamRequestHandler<Stream3,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream3 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,3); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>),typeof(IBehavior2<,>),typeof(IBehavior3<,>))]
public static partial class IPing3 { private static ValueTask<int> HandleAsync(Ping3 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler] [I.Behaviors(typeof(ISBehavior1<,>),typeof(ISBehavior2<,>),typeof(ISBehavior3<,>))]
public static partial class IStream3 { private static IAsyncEnumerable<int> HandleAsync(Stream3 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed record Ping5(int Value = 41, Probe? Probe = null) : ILevel5, Z.IRequest<int>, M.IRequest<int>, G.IRequest<int>, D.IRequest<Ping5, ValueTask<int>>;
public sealed record Stream5(int Count, bool Asynchronous, Probe? Probe = null) : ILevel5, Z.IStreamRequest<int>, M.IStreamRequest<int>, G.IStreamRequest<int>, DS.IStreamRequest<Stream5, int>;
public sealed class ZPing5 : Z.IRequestHandler<Ping5,int> { public ValueTask<int> HandleAsync(Ping5 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MPing5 : M.IRequestHandler<Ping5,int> { public Task<int> Handle(Ping5 r,CancellationToken t) => Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class GPing5 : G.IRequestHandler<Ping5,int> { public ValueTask<int> Handle(Ping5 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DPing5 : D.IRequestHandler<Ping5,ValueTask<int>> { public ValueTask<int> Handle(Ping5 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZStream5 : Z.IStreamRequestHandler<Stream5,int> { public IAsyncEnumerable<int> HandleAsync(Stream5 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class MStream5 : M.IStreamRequestHandler<Stream5,int> { public IAsyncEnumerable<int> Handle(Stream5 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class GStream5 : G.IStreamRequestHandler<Stream5,int> { public IAsyncEnumerable<int> Handle(Stream5 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DStream5 : DS.IStreamRequestHandler<Stream5,int> { public IAsyncEnumerable<int> Handle(Stream5 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class DBehavior5_1 : D.IPipelineBehavior<Ping5,ValueTask<int>>
{ public required D.IRequestHandler<Ping5,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping5 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior5_1 : DS.IStreamPipelineBehavior<Stream5,int>
{ public required DS.IStreamRequestHandler<Stream5,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream5 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior5_2 : D.IPipelineBehavior<Ping5,ValueTask<int>>
{ public required D.IRequestHandler<Ping5,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping5 r,CancellationToken t) { Work.Enter(r,2); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior5_2 : DS.IStreamPipelineBehavior<Stream5,int>
{ public required DS.IStreamRequestHandler<Stream5,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream5 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,2); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior5_3 : D.IPipelineBehavior<Ping5,ValueTask<int>>
{ public required D.IRequestHandler<Ping5,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping5 r,CancellationToken t) { Work.Enter(r,3); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior5_3 : DS.IStreamPipelineBehavior<Stream5,int>
{ public required DS.IStreamRequestHandler<Stream5,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream5 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,3); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior5_4 : D.IPipelineBehavior<Ping5,ValueTask<int>>
{ public required D.IRequestHandler<Ping5,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping5 r,CancellationToken t) { Work.Enter(r,4); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior5_4 : DS.IStreamPipelineBehavior<Stream5,int>
{ public required DS.IStreamRequestHandler<Stream5,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream5 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,4); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class DBehavior5_5 : D.IPipelineBehavior<Ping5,ValueTask<int>>
{ public required D.IRequestHandler<Ping5,ValueTask<int>> NextPipeline {get;set;} public ValueTask<int> Handle(Ping5 r,CancellationToken t) { Work.Enter(r,5); return NextPipeline.Handle(r,t); } }
public sealed class DSBehavior5_5 : DS.IStreamPipelineBehavior<Stream5,int>
{ public required DS.IStreamRequestHandler<Stream5,int> NextPipeline {get;set;} public async IAsyncEnumerable<int> Handle(Stream5 r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,5); await foreach(var item in NextPipeline.Handle(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>),typeof(IBehavior2<,>),typeof(IBehavior3<,>),typeof(IBehavior4<,>),typeof(IBehavior5<,>))]
public static partial class IPing5 { private static ValueTask<int> HandleAsync(Ping5 r,CancellationToken t) => new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler] [I.Behaviors(typeof(ISBehavior1<,>),typeof(ISBehavior2<,>),typeof(ISBehavior3<,>),typeof(ISBehavior4<,>),typeof(ISBehavior5<,>))]
public static partial class IStream5 { private static IAsyncEnumerable<int> HandleAsync(Stream5 r,CancellationToken t) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t); }
public sealed class ZBehavior1<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel1
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,1); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior1<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel1
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,1); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior1<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,1); return next(t); } }
public sealed class MSBehavior1<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior1<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,1); return next(r,t); } }
public sealed class GSBehavior1<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior1<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,1); return Next(r,t); } }
public sealed class ISBehavior1<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,1); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class ZBehavior2<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel2
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,2); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior2<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel2
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,2); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior2<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,2); return next(t); } }
public sealed class MSBehavior2<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,2); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior2<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,2); return next(r,t); } }
public sealed class GSBehavior2<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,2); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior2<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,2); return Next(r,t); } }
public sealed class ISBehavior2<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,2); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class ZBehavior3<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel3
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,3); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior3<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel3
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,3); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior3<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,3); return next(t); } }
public sealed class MSBehavior3<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,3); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior3<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,3); return next(r,t); } }
public sealed class GSBehavior3<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,3); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior3<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,3); return Next(r,t); } }
public sealed class ISBehavior3<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,3); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class ZBehavior4<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel4
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,4); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior4<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel4
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,4); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior4<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,4); return next(t); } }
public sealed class MSBehavior4<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,4); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior4<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,4); return next(r,t); } }
public sealed class GSBehavior4<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,4); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior4<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,4); return Next(r,t); } }
public sealed class ISBehavior4<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,4); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class ZBehavior5<T,R> : Z.IPipelineBehavior<T,R> where T : Z.IRequest<R>, ILevel5
{ public ValueTask<R> HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T,R> { Work.Enter(r,5); return next.InvokeAsync(r,t); } }
public sealed class ZSBehavior5<T,R> : Z.IStreamPipelineBehavior<T,R> where T : Z.IStreamRequest<R>, ILevel5
{ public async IAsyncEnumerable<R> HandleAsync<TNext>(T r,TNext next,[EnumeratorCancellation] CancellationToken t) where TNext:struct,Z.IStreamContinuation<T,R> { Work.Enter(r,5); await foreach(var item in next.InvokeAsync(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class MBehavior5<T,R> : M.IPipelineBehavior<T,R> where T : notnull,IWork
{ public Task<R> Handle(T r,M.RequestHandlerDelegate<R> next,CancellationToken t) { Work.Enter(r,5); return next(t); } }
public sealed class MSBehavior5<T,R> : M.IStreamPipelineBehavior<T,R> where T : notnull,IWork
{ public async IAsyncEnumerable<R> Handle(T r,M.StreamHandlerDelegate<R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,5); await foreach(var item in next().WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class GBehavior5<T,R> : G.IPipelineBehavior<T,R> where T : G.IMessage,IWork
{ public ValueTask<R> Handle(T r,G.MessageHandlerDelegate<T,R> next,CancellationToken t) { Work.Enter(r,5); return next(r,t); } }
public sealed class GSBehavior5<T,R> : G.IStreamPipelineBehavior<T,R> where T : G.IStreamMessage,IWork
{ public async IAsyncEnumerable<R> Handle(T r,G.StreamHandlerDelegate<T,R> next,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,5); await foreach(var item in next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed class IBehavior5<T,R> : I.Behavior<T,R> where T:IWork
{ public override ValueTask<R> HandleAsync(T r,CancellationToken t) { Work.Enter(r,5); return Next(r,t); } }
public sealed class ISBehavior5<T,R> : I.StreamingBehavior<T,R> where T:IWork
{ public override async IAsyncEnumerable<R> HandleAsync(T r,[EnumeratorCancellation] CancellationToken t) { Work.Enter(r,5); await foreach(var item in Next(r,t).WithCancellation(t).ConfigureAwait(false)) yield return item; } }
public sealed record Void0(Counter Counter, Probe? Probe = null) : IWork, Z.IRequest, M.IRequest, G.IRequest, D.IRequest<Void0,ValueTask<ValueTuple>>;
public sealed class ZVoid0 : Z.IRequestHandler<Void0> { public ValueTask HandleAsync(Void0 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class MVoid0 : M.IRequestHandler<Void0> { public Task Handle(Void0 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return Task.CompletedTask; } }
public sealed class GVoid0 : G.IRequestHandler<Void0> { public ValueTask<G.Unit> Handle(Void0 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVoid0 : D.IRequestHandler<Void0,ValueTask<ValueTuple>> { public ValueTask<ValueTuple> Handle(Void0 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
[I.Handler] [I.Behaviors()] public static partial class IVoid0 { private static ValueTask HandleAsync(Void0 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed record Void1(Counter Counter, Probe? Probe = null) : ILevel1, Z.IRequest, M.IRequest, G.IRequest, D.IRequest<Void1,ValueTask<ValueTuple>>;
public sealed class ZVoid1 : Z.IRequestHandler<Void1> { public ValueTask HandleAsync(Void1 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class MVoid1 : M.IRequestHandler<Void1> { public Task Handle(Void1 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return Task.CompletedTask; } }
public sealed class GVoid1 : G.IRequestHandler<Void1> { public ValueTask<G.Unit> Handle(Void1 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVoid1 : D.IRequestHandler<Void1,ValueTask<ValueTuple>> { public ValueTask<ValueTuple> Handle(Void1 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>))] public static partial class IVoid1 { private static ValueTask HandleAsync(Void1 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVBehavior1_1 : D.IPipelineBehavior<Void1,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void1,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void1 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed record Void3(Counter Counter, Probe? Probe = null) : ILevel3, Z.IRequest, M.IRequest, G.IRequest, D.IRequest<Void3,ValueTask<ValueTuple>>;
public sealed class ZVoid3 : Z.IRequestHandler<Void3> { public ValueTask HandleAsync(Void3 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class MVoid3 : M.IRequestHandler<Void3> { public Task Handle(Void3 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return Task.CompletedTask; } }
public sealed class GVoid3 : G.IRequestHandler<Void3> { public ValueTask<G.Unit> Handle(Void3 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVoid3 : D.IRequestHandler<Void3,ValueTask<ValueTuple>> { public ValueTask<ValueTuple> Handle(Void3 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>),typeof(IBehavior2<,>),typeof(IBehavior3<,>))] public static partial class IVoid3 { private static ValueTask HandleAsync(Void3 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVBehavior3_1 : D.IPipelineBehavior<Void3,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void3,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void3 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior3_2 : D.IPipelineBehavior<Void3,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void3,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void3 r,CancellationToken t) { Work.Enter(r,2); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior3_3 : D.IPipelineBehavior<Void3,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void3,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void3 r,CancellationToken t) { Work.Enter(r,3); return NextPipeline.Handle(r,t); } }
public sealed record Void5(Counter Counter, Probe? Probe = null) : ILevel5, Z.IRequest, M.IRequest, G.IRequest, D.IRequest<Void5,ValueTask<ValueTuple>>;
public sealed class ZVoid5 : Z.IRequestHandler<Void5> { public ValueTask HandleAsync(Void5 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class MVoid5 : M.IRequestHandler<Void5> { public Task Handle(Void5 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return Task.CompletedTask; } }
public sealed class GVoid5 : G.IRequestHandler<Void5> { public ValueTask<G.Unit> Handle(Void5 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVoid5 : D.IRequestHandler<Void5,ValueTask<ValueTuple>> { public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
[I.Handler] [I.Behaviors(typeof(IBehavior1<,>),typeof(IBehavior2<,>),typeof(IBehavior3<,>),typeof(IBehavior4<,>),typeof(IBehavior5<,>))] public static partial class IVoid5 { private static ValueTask HandleAsync(Void5 r,CancellationToken t) { Work.Void(r.Counter,r.Probe,t); return default; } }
public sealed class DVBehavior5_1 : D.IPipelineBehavior<Void5,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void5,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Enter(r,1); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior5_2 : D.IPipelineBehavior<Void5,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void5,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Enter(r,2); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior5_3 : D.IPipelineBehavior<Void5,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void5,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Enter(r,3); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior5_4 : D.IPipelineBehavior<Void5,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void5,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Enter(r,4); return NextPipeline.Handle(r,t); } }
public sealed class DVBehavior5_5 : D.IPipelineBehavior<Void5,ValueTask<ValueTuple>>
{ public required D.IRequestHandler<Void5,ValueTask<ValueTuple>> NextPipeline {get;set;} public ValueTask<ValueTuple> Handle(Void5 r,CancellationToken t) { Work.Enter(r,5); return NextPipeline.Handle(r,t); } }
public sealed class ZVBehavior1<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel1
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,1); return next.InvokeAsync(r,t); } }
public sealed class ZVBehavior2<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel2
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,2); return next.InvokeAsync(r,t); } }
public sealed class ZVBehavior3<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel3
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,3); return next.InvokeAsync(r,t); } }
public sealed class ZVBehavior4<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel4
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,4); return next.InvokeAsync(r,t); } }
public sealed class ZVBehavior5<T> : Z.IPipelineBehavior<T> where T:Z.IRequest,ILevel5
{ public ValueTask HandleAsync<TNext>(T r,TNext next,CancellationToken t) where TNext:struct,Z.IRequestContinuation<T> { Work.Enter(r,5); return next.InvokeAsync(r,t); } }
public sealed record ClosedGeneric<T>(int Value=41, Probe? Probe=null) : IWork,Z.IRequest<int>,M.IRequest<int>,D.IRequest<ClosedGeneric<T>,ValueTask<int>>;
public sealed record OpenGeneric<T>(int Value=41, Probe? Probe=null) : IWork,Z.IRequest<int>,M.IRequest<int>,D.IRequest<OpenGeneric<T>,ValueTask<int>>;
public sealed class ZClosed : Z.IRequestHandler<ClosedGeneric<int>,int> { public ValueTask<int> HandleAsync(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MClosed : M.IRequestHandler<ClosedGeneric<int>,int> { public Task<int> Handle(ClosedGeneric<int> r,CancellationToken t)=>Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DClosed : D.IRequestHandler<ClosedGeneric<int>,ValueTask<int>> { public ValueTask<int> Handle(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
[I.Handler][I.Behaviors()] public static partial class IClosed { private static ValueTask<int> HandleAsync(ClosedGeneric<int> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class ZOpen<T> : Z.IRequestHandler<OpenGeneric<T>,int> { public ValueTask<int> HandleAsync(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
public sealed class MOpen<T> : M.IRequestHandler<OpenGeneric<T>,int> { public Task<int> Handle(OpenGeneric<T> r,CancellationToken t)=>Task.FromResult(Work.Handle(r.Value,r.Probe,t)); }
public sealed class DOpen<T> : D.IRequestHandler<OpenGeneric<T>,ValueTask<int>> { public ValueTask<int> Handle(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
#if PROBE_IMMEDIATE_OPEN
[I.Handler][I.Behaviors()] public static partial class IOpen<T> { private static ValueTask<int> HandleAsync(OpenGeneric<T> r,CancellationToken t)=>new(Work.Handle(r.Value,r.Probe,t)); }
#endif
public sealed record Notice0(Counter Counter, Probe? Probe=null, bool Asynchronous=false) : Z.INotification,M.INotification,G.INotification,DispatchR.Abstractions.Notification.INotification;
public sealed record Notice1(Counter Counter, Probe? Probe=null, bool Asynchronous=false) : Z.INotification,M.INotification,G.INotification,DispatchR.Abstractions.Notification.INotification;
public sealed class ZNotice1_1 : Z.INotificationHandler<Notice1> { public ValueTask HandleAsync(Notice1 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class MNotice1_1 : M.INotificationHandler<Notice1> { public Task Handle(Notice1 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t).AsTask(); }
public sealed class GNotice1_1 : G.INotificationHandler<Notice1> { public ValueTask Handle(Notice1 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class DNotice1_1 : DispatchR.Abstractions.Notification.INotificationHandler<Notice1> { public ValueTask Handle(Notice1 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed record Notice4(Counter Counter, Probe? Probe=null, bool Asynchronous=false) : Z.INotification,M.INotification,G.INotification,DispatchR.Abstractions.Notification.INotification;
public sealed class ZNotice4_1 : Z.INotificationHandler<Notice4> { public ValueTask HandleAsync(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class MNotice4_1 : M.INotificationHandler<Notice4> { public Task Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t).AsTask(); }
public sealed class GNotice4_1 : G.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class DNotice4_1 : DispatchR.Abstractions.Notification.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class ZNotice4_2 : Z.INotificationHandler<Notice4> { public ValueTask HandleAsync(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class MNotice4_2 : M.INotificationHandler<Notice4> { public Task Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t).AsTask(); }
public sealed class GNotice4_2 : G.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class DNotice4_2 : DispatchR.Abstractions.Notification.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class ZNotice4_3 : Z.INotificationHandler<Notice4> { public ValueTask HandleAsync(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class MNotice4_3 : M.INotificationHandler<Notice4> { public Task Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t).AsTask(); }
public sealed class GNotice4_3 : G.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class DNotice4_3 : DispatchR.Abstractions.Notification.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class ZNotice4_4 : Z.INotificationHandler<Notice4> { public ValueTask HandleAsync(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed class MNotice4_4 : M.INotificationHandler<Notice4> { public Task Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t).AsTask(); }
public sealed class GNotice4_4 : G.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed class DNotice4_4 : DispatchR.Abstractions.Notification.INotificationHandler<Notice4> { public ValueTask Handle(Notice4 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed record Notice16(Counter Counter, Probe? Probe=null, bool Asynchronous=false) : Z.INotification,M.INotification,G.INotification,DispatchR.Abstractions.Notification.INotification;
public sealed class ZNotice16_1 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class MNotice16_1 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t).AsTask(); }
public sealed class GNotice16_1 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class DNotice16_1 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,1,t); }
public sealed class ZNotice16_2 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class MNotice16_2 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t).AsTask(); }
public sealed class GNotice16_2 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class DNotice16_2 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,2,t); }
public sealed class ZNotice16_3 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class MNotice16_3 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t).AsTask(); }
public sealed class GNotice16_3 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class DNotice16_3 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,3,t); }
public sealed class ZNotice16_4 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed class MNotice16_4 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t).AsTask(); }
public sealed class GNotice16_4 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed class DNotice16_4 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,4,t); }
public sealed class ZNotice16_5 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,5,t); }
public sealed class MNotice16_5 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,5,t).AsTask(); }
public sealed class GNotice16_5 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,5,t); }
public sealed class DNotice16_5 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,5,t); }
public sealed class ZNotice16_6 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,6,t); }
public sealed class MNotice16_6 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,6,t).AsTask(); }
public sealed class GNotice16_6 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,6,t); }
public sealed class DNotice16_6 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,6,t); }
public sealed class ZNotice16_7 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,7,t); }
public sealed class MNotice16_7 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,7,t).AsTask(); }
public sealed class GNotice16_7 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,7,t); }
public sealed class DNotice16_7 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,7,t); }
public sealed class ZNotice16_8 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,8,t); }
public sealed class MNotice16_8 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,8,t).AsTask(); }
public sealed class GNotice16_8 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,8,t); }
public sealed class DNotice16_8 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,8,t); }
public sealed class ZNotice16_9 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,9,t); }
public sealed class MNotice16_9 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,9,t).AsTask(); }
public sealed class GNotice16_9 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,9,t); }
public sealed class DNotice16_9 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,9,t); }
public sealed class ZNotice16_10 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,10,t); }
public sealed class MNotice16_10 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,10,t).AsTask(); }
public sealed class GNotice16_10 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,10,t); }
public sealed class DNotice16_10 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,10,t); }
public sealed class ZNotice16_11 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,11,t); }
public sealed class MNotice16_11 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,11,t).AsTask(); }
public sealed class GNotice16_11 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,11,t); }
public sealed class DNotice16_11 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,11,t); }
public sealed class ZNotice16_12 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,12,t); }
public sealed class MNotice16_12 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,12,t).AsTask(); }
public sealed class GNotice16_12 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,12,t); }
public sealed class DNotice16_12 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,12,t); }
public sealed class ZNotice16_13 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,13,t); }
public sealed class MNotice16_13 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,13,t).AsTask(); }
public sealed class GNotice16_13 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,13,t); }
public sealed class DNotice16_13 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,13,t); }
public sealed class ZNotice16_14 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,14,t); }
public sealed class MNotice16_14 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,14,t).AsTask(); }
public sealed class GNotice16_14 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,14,t); }
public sealed class DNotice16_14 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,14,t); }
public sealed class ZNotice16_15 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,15,t); }
public sealed class MNotice16_15 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,15,t).AsTask(); }
public sealed class GNotice16_15 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,15,t); }
public sealed class DNotice16_15 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,15,t); }
public sealed class ZNotice16_16 : Z.INotificationHandler<Notice16> { public ValueTask HandleAsync(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,16,t); }
public sealed class MNotice16_16 : M.INotificationHandler<Notice16> { public Task Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,16,t).AsTask(); }
public sealed class GNotice16_16 : G.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,16,t); }
public sealed class DNotice16_16 : DispatchR.Abstractions.Notification.INotificationHandler<Notice16> { public ValueTask Handle(Notice16 r,CancellationToken t)=>Work.Notify(r.Counter,r.Probe,r.Asynchronous,16,t); }
public static class Registration
{
#if MATCHED_SCOPED
    public static string MediatorLifetime => "Scoped";
    public static IEnumerable<string> MediatorLifetimes => ["Scoped"];
#else
    public static string MediatorLifetime => "Singleton";
    public static IEnumerable<string> MediatorLifetimes => ["Default","Singleton"];
#endif
    public static ServiceProvider Create(string library, string lifetime = "Default")
    {
        var s = new ServiceCollection();
        s.AddLogging();
        switch(library)
        {
            case "Zendiator":
                s.AddZendiator(c => {
                    c.Namespace = "Competitive.Generated";
                    c.ServiceLifetime = lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Transient" ? ServiceLifetime.Transient : ServiceLifetime.Scoped;
                    c.RegisterServicesFromAssemblyContaining<Ping0>();
c.AddOpenBehavior(typeof(ZBehavior1<,>), order: 1); c.AddOpenStreamBehavior(typeof(ZSBehavior1<,>), order: 11);
c.AddOpenBehavior(typeof(ZVBehavior1<>), order: 21);
c.AddOpenBehavior(typeof(ZBehavior2<,>), order: 2); c.AddOpenStreamBehavior(typeof(ZSBehavior2<,>), order: 12);
c.AddOpenBehavior(typeof(ZVBehavior2<>), order: 22);
c.AddOpenBehavior(typeof(ZBehavior3<,>), order: 3); c.AddOpenStreamBehavior(typeof(ZSBehavior3<,>), order: 13);
c.AddOpenBehavior(typeof(ZVBehavior3<>), order: 23);
c.AddOpenBehavior(typeof(ZBehavior4<,>), order: 4); c.AddOpenStreamBehavior(typeof(ZSBehavior4<,>), order: 14);
c.AddOpenBehavior(typeof(ZVBehavior4<>), order: 24);
c.AddOpenBehavior(typeof(ZBehavior5<,>), order: 5); c.AddOpenStreamBehavior(typeof(ZSBehavior5<,>), order: 15);
c.AddOpenBehavior(typeof(ZVBehavior5<>), order: 25);
                });
                break;
            case "MediatRHistorical":
                s.AddMediatR(c => { c.RegisterServicesFromAssemblyContaining<Ping0>(); if (lifetime != "Default") c.Lifetime = lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Scoped" ? ServiceLifetime.Scoped : ServiceLifetime.Transient; });
                break;
            case "Mediator":
#if MATCHED_SCOPED
                s.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Scoped; });
#else
                s.AddMediator(c => { c.ServiceLifetime = ServiceLifetime.Singleton; });
#endif
                break;
            case "DispatchR":
                if (lifetime is not ("Default" or "Scoped")) throw new NotSupportedException("DispatchR entry lifetime is fixed by AddDispatchR.");
                break;
            case "Immediate":
                if (lifetime == "Default") s.AddCompetitiveHandlers();
                else s.AddCompetitiveHandlers(lifetime == "Singleton" ? ServiceLifetime.Singleton : lifetime == "Transient" ? ServiceLifetime.Transient : ServiceLifetime.Scoped);
                break;
            case "Direct": break;
            default: throw new ArgumentOutOfRangeException(nameof(library));
        }
        RegistrationAudit.Record(s, library, lifetime, "after-library");
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping1,int>,MBehavior1<Ping1,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream1,int>,MSBehavior1<Stream1,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping1,int>,GBehavior1<Ping1,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream1,int>,GSBehavior1<Stream1,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping3,int>,MBehavior1<Ping3,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream3,int>,MSBehavior1<Stream3,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping3,int>,GBehavior1<Ping3,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream3,int>,GSBehavior1<Stream3,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping3,int>,MBehavior2<Ping3,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream3,int>,MSBehavior2<Stream3,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping3,int>,GBehavior2<Ping3,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream3,int>,GSBehavior2<Stream3,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping3,int>,MBehavior3<Ping3,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream3,int>,MSBehavior3<Stream3,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping3,int>,GBehavior3<Ping3,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream3,int>,GSBehavior3<Stream3,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping5,int>,MBehavior1<Ping5,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream5,int>,MSBehavior1<Stream5,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping5,int>,GBehavior1<Ping5,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream5,int>,GSBehavior1<Stream5,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping5,int>,MBehavior2<Ping5,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream5,int>,MSBehavior2<Stream5,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping5,int>,GBehavior2<Ping5,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream5,int>,GSBehavior2<Stream5,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping5,int>,MBehavior3<Ping5,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream5,int>,MSBehavior3<Stream5,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping5,int>,GBehavior3<Ping5,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream5,int>,GSBehavior3<Stream5,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping5,int>,MBehavior4<Ping5,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream5,int>,MSBehavior4<Stream5,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping5,int>,GBehavior4<Ping5,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream5,int>,GSBehavior4<Stream5,int>>(); }
if(library == "MediatRHistorical") { s.AddTransient<M.IPipelineBehavior<Ping5,int>,MBehavior5<Ping5,int>>(); s.AddTransient<M.IStreamPipelineBehavior<Stream5,int>,MSBehavior5<Stream5,int>>(); }
if(library == "Mediator") { s.AddSingleton<G.IPipelineBehavior<Ping5,int>,GBehavior5<Ping5,int>>(); s.AddSingleton<G.IStreamPipelineBehavior<Stream5,int>,GSBehavior5<Stream5,int>>(); }
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void1,M.Unit>,MBehavior1<Void1,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void1,G.Unit>,GBehavior1<Void1,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void3,M.Unit>,MBehavior1<Void3,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void3,G.Unit>,GBehavior1<Void3,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void3,M.Unit>,MBehavior2<Void3,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void3,G.Unit>,GBehavior2<Void3,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void3,M.Unit>,MBehavior3<Void3,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void3,G.Unit>,GBehavior3<Void3,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void5,M.Unit>,MBehavior1<Void5,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void5,G.Unit>,GBehavior1<Void5,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void5,M.Unit>,MBehavior2<Void5,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void5,G.Unit>,GBehavior2<Void5,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void5,M.Unit>,MBehavior3<Void5,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void5,G.Unit>,GBehavior3<Void5,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void5,M.Unit>,MBehavior4<Void5,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void5,G.Unit>,GBehavior4<Void5,G.Unit>>();
if(library == "MediatRHistorical") s.AddTransient<M.IPipelineBehavior<Void5,M.Unit>,MBehavior5<Void5,M.Unit>>();
if(library == "Mediator") s.AddSingleton<G.IPipelineBehavior<Void5,G.Unit>,GBehavior5<Void5,G.Unit>>();
if(library == "MediatRHistorical") {
    s.AddTransient<M.IRequestHandler<OpenGeneric<int>,int>,MOpen<int>>();
    s.AddTransient<M.IRequestHandler<OpenGeneric<string>,int>,MOpen<string>>();
}
if(library == "DispatchR") {
    s.AddScoped<D.IRequestHandler<OpenGeneric<int>,ValueTask<int>>,DOpen<int>>();
    s.AddScoped<D.IRequestHandler<OpenGeneric<string>,ValueTask<int>>,DOpen<string>>();
}
if(library == "DispatchR") s.AddDispatchR(c => { c.Assemblies.Add(typeof(Ping0).Assembly); c.ExcludeHandlers=[typeof(DOpen<>)]; c.PipelineOrder=[typeof(DBehavior1_1),typeof(DSBehavior1_1),typeof(DVBehavior1_1),typeof(DBehavior3_1),typeof(DSBehavior3_1),typeof(DVBehavior3_1),typeof(DBehavior3_2),typeof(DSBehavior3_2),typeof(DVBehavior3_2),typeof(DBehavior3_3),typeof(DSBehavior3_3),typeof(DVBehavior3_3),typeof(DBehavior5_1),typeof(DSBehavior5_1),typeof(DVBehavior5_1),typeof(DBehavior5_2),typeof(DSBehavior5_2),typeof(DVBehavior5_2),typeof(DBehavior5_3),typeof(DSBehavior5_3),typeof(DVBehavior5_3),typeof(DBehavior5_4),typeof(DSBehavior5_4),typeof(DVBehavior5_4),typeof(DBehavior5_5),typeof(DSBehavior5_5),typeof(DVBehavior5_5)]; });
if (library == "Mediator" && lifetime != "Default" && lifetime != MediatorLifetime)
    throw new NotSupportedException("Mediator lifetime must match its compile-time generation configuration.");
RegistrationAudit.Record(s, library, lifetime, "before-provider");
return s.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); } }
[MemoryDiagnoser]
public class DirectSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private ZPing0 entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct",Lifetime); scope=provider.CreateScope(); entry=new ZPing0(); ChildEvidence.Record("DirectSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping0 r,CancellationToken t=default) => new ValueTask<int>(Work.Handle(r.Value,r.Probe,t));
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=new ZPing0(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=new ZPing0();
        var r=request; var t=CancellationToken.None;
        return await new ValueTask<int>(Work.Handle(r.Value,r.Probe,t));
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=new ZPing0();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await new ValueTask<int>(Work.Handle(r.Value,r.Probe,t));
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class DirectStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private ZPing0 entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(Count,Asynchronous); ChildEvidence.Record("DirectStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => Work.Stream(r.Count,r.Asynchronous,r.Probe,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ZendiatorSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); ChildEvidence.Record("ZendiatorSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping0 r,CancellationToken t=default) => entry.SendAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None;
        return await entry.SendAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.SendAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ZendiatorStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(Count,Asynchronous); ChildEvidence.Record("ZendiatorStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => entry.StreamAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ZendiatorSend1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private readonly Ping1 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); ChildEvidence.Record("ZendiatorSend1", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping1 r,CancellationToken t=default) => entry.SendAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None;
        return await entry.SendAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.SendAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ZendiatorStream1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private Stream1 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(Count,Asynchronous); ChildEvidence.Record("ZendiatorStream1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream1 r,CancellationToken t=default) => entry.StreamAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ZendiatorSend3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private readonly Ping3 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); ChildEvidence.Record("ZendiatorSend3", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping3 r,CancellationToken t=default) => entry.SendAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None;
        return await entry.SendAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.SendAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ZendiatorStream3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private Stream3 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(Count,Asynchronous); ChildEvidence.Record("ZendiatorStream3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream3 r,CancellationToken t=default) => entry.StreamAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ZendiatorSend5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private readonly Ping5 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); ChildEvidence.Record("ZendiatorSend5", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping5 r,CancellationToken t=default) => entry.SendAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None;
        return await entry.SendAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.SendAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ZendiatorStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private Competitive.Generated.IZendiator entry = null!;
    private Stream5 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(Count,Asynchronous); ChildEvidence.Record("ZendiatorStream5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream5 r,CancellationToken t=default) => entry.StreamAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatRHistoricalSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); ChildEvidence.Record("MediatRHistoricalSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public Task<int> Invoke(Ping0 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public Task<int> Typed() => Invoke(request);
    [Benchmark] public Task<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatRHistoricalStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatRHistoricalStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatRHistoricalSend1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private readonly Ping1 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); ChildEvidence.Record("MediatRHistoricalSend1", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public Task<int> Invoke(Ping1 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public Task<int> Typed() => Invoke(request);
    [Benchmark] public Task<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatRHistoricalStream1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private Stream1 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatRHistoricalStream1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream1 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatRHistoricalSend3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private readonly Ping3 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); ChildEvidence.Record("MediatRHistoricalSend3", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public Task<int> Invoke(Ping3 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public Task<int> Typed() => Invoke(request);
    [Benchmark] public Task<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatRHistoricalStream3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private Stream3 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatRHistoricalStream3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream3 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatRHistoricalSend5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private readonly Ping5 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); ChildEvidence.Record("MediatRHistoricalSend5", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public Task<int> Invoke(Ping5 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public Task<int> Typed() => Invoke(request);
    [Benchmark] public Task<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<M.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatRHistoricalStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private M.IMediator entry = null!;
    private Stream5 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatRHistoricalStream5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream5 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatorSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => Registration.MediatorLifetimes;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); ChildEvidence.Record("MediatorSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping0 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatorStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatorStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatorSend1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private readonly Ping1 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => Registration.MediatorLifetimes;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); ChildEvidence.Record("MediatorSend1", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping1 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatorStream1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private Stream1 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatorStream1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream1 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatorSend3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private readonly Ping3 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => Registration.MediatorLifetimes;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); ChildEvidence.Record("MediatorSend3", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping3 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatorStream3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private Stream3 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatorStream3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream3 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class MediatorSend5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private readonly Ping5 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => Registration.MediatorLifetimes;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); ChildEvidence.Record("MediatorSend5", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping5 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<G.Mediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class MediatorStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private G.Mediator entry = null!;
    private Stream5 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("MediatorStream5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream5 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class DispatchRSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); ChildEvidence.Record("DispatchRSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping0 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class DispatchRStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("DispatchRStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class DispatchRSend1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private readonly Ping1 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); ChildEvidence.Record("DispatchRSend1", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping1 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class DispatchRStream1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private Stream1 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("DispatchRStream1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream1 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class DispatchRSend3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private readonly Ping3 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); ChildEvidence.Record("DispatchRSend3", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping3 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class DispatchRStream3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private Stream3 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("DispatchRStream3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream3 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class DispatchRSend5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private readonly Ping5 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); ChildEvidence.Record("DispatchRSend5", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping5 r,CancellationToken t=default) => entry.Send(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None;
        return await entry.Send(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<DispatchR.IMediator>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.Send(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class DispatchRStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private DispatchR.IMediator entry = null!;
    private Stream5 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(Count,Asynchronous); ChildEvidence.Record("DispatchRStream5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream5 r,CancellationToken t=default) => entry.CreateStream(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ImmediateSend0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IPing0.Handler entry = null!;
    private readonly Ping0 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); ChildEvidence.Record("ImmediateSend0", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping0 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<IPing0.Handler>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing0.Handler>();
        var r=request; var t=CancellationToken.None;
        return await entry.HandleAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing0.Handler>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.HandleAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ImmediateStream0
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IStream0.Handler entry = null!;
    private Stream0 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IStream0.Handler>(); request=new(Count,Asynchronous); ChildEvidence.Record("ImmediateStream0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream0 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ImmediateSend1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IPing1.Handler entry = null!;
    private readonly Ping1 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IPing1.Handler>(); ChildEvidence.Record("ImmediateSend1", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping1 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<IPing1.Handler>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing1.Handler>();
        var r=request; var t=CancellationToken.None;
        return await entry.HandleAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing1.Handler>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.HandleAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ImmediateStream1
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IStream1.Handler entry = null!;
    private Stream1 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IStream1.Handler>(); request=new(Count,Asynchronous); ChildEvidence.Record("ImmediateStream1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream1 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ImmediateSend3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IPing3.Handler entry = null!;
    private readonly Ping3 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IPing3.Handler>(); ChildEvidence.Record("ImmediateSend3", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping3 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<IPing3.Handler>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing3.Handler>();
        var r=request; var t=CancellationToken.None;
        return await entry.HandleAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing3.Handler>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.HandleAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ImmediateStream3
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IStream3.Handler entry = null!;
    private Stream3 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IStream3.Handler>(); request=new(Count,Asynchronous); ChildEvidence.Record("ImmediateStream3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream3 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser]
public class ImmediateSend5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IPing5.Handler entry = null!;
    private readonly Ping5 request = new();
    [ParamsSource(nameof(Lifetimes))] public string Lifetime {get;set;} = "Default";
    public IEnumerable<string> Lifetimes => ["Default","Scoped","Singleton"];
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate",Lifetime); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IPing5.Handler>(); ChildEvidence.Record("ImmediateSend5", Lifetime); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public ValueTask<int> Invoke(Ping5 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public ValueTask<int> Typed() => Invoke(request);
    [Benchmark] public ValueTask<int> ResolveSend() { entry=scope.ServiceProvider.GetRequiredService<IPing5.Handler>(); return Invoke(request); }
    [Benchmark] public async ValueTask<int> ScopeK1()
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing5.Handler>();
        var r=request; var t=CancellationToken.None;
        return await entry.HandleAsync(r,t);
    }
    public async ValueTask<int> ScopeMany(int count)
    {
        using var local=provider.CreateScope();
        var entry=local.ServiceProvider.GetRequiredService<IPing5.Handler>();
        var r=request; var t=CancellationToken.None; int sum=0;
        for(int i=0;i<count;i++) sum+=await entry.HandleAsync(r,t);
        return sum;
    }
    [Benchmark] public ValueTask<int> ScopeK10() => ScopeMany(10);
    [Benchmark] public ValueTask<int> ScopeK100() => ScopeMany(100);
}
[MemoryDiagnoser]
public class ImmediateStream5
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IStream5.Handler entry = null!;
    private Stream5 request = null!;
    [Params(0,1,16,1024)] public int Count {get;set;}
    [Params(false,true)] public bool Asynchronous {get;set;}
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IStream5.Handler>(); request=new(Count,Asynchronous); ChildEvidence.Record("ImmediateStream5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public IAsyncEnumerable<int> Invoke(Stream5 r,CancellationToken t=default) => entry.HandleAsync(r,t);
    [Benchmark] public IAsyncEnumerable<int> Creation() => Invoke(request);
    [Benchmark] public async ValueTask<int> Full() { int sum=0; await foreach(var item in Invoke(request).ConfigureAwait(false)) sum+=item; return sum; }
    [Benchmark] public async ValueTask<bool> First() { await using var e=Invoke(request).GetAsyncEnumerator(); return await e.MoveNextAsync(); }
    [Benchmark] public async ValueTask<int> EarlyBreak() { await foreach(var item in Invoke(request).ConfigureAwait(false)) return item; return -1; }
    [Benchmark] public async ValueTask<bool> Cancellation()
    {
        using var cts=new CancellationTokenSource();
        await using var e=Invoke(request,cts.Token).GetAsyncEnumerator();
        await e.MoveNextAsync();
        cts.Cancel();
        try { await e.MoveNextAsync(); return false; } catch(OperationCanceledException) { return true; }
    }
}
[MemoryDiagnoser] public class DirectClosed
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private ClosedGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(); ChildEvidence.Record("DirectClosed", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(ClosedGeneric<int> r,CancellationToken t=default) { return Work.Handle(r.Value,r.Probe,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectOpen
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private OpenGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(); ChildEvidence.Record("DirectOpen", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(OpenGeneric<int> r,CancellationToken t=default) { return Work.Handle(r.Value,r.Probe,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(counter); ChildEvidence.Record("DirectVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { Work.Void(r.Counter,r.Probe,t); await ValueTask.CompletedTask; }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectNotification0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private Notice0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(counter); ChildEvidence.Record("DirectNotification0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice0 r,CancellationToken t=default) { for(int i=1;i<=0;i++) await Work.Notify(r.Counter,r.Probe,r.Asynchronous,i,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectNotification1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private Notice1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(counter); ChildEvidence.Record("DirectNotification1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice1 r,CancellationToken t=default) { for(int i=1;i<=1;i++) await Work.Notify(r.Counter,r.Probe,r.Asynchronous,i,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectNotification4
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private Notice4 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(counter); ChildEvidence.Record("DirectNotification4", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice4 r,CancellationToken t=default) { for(int i=1;i<=4;i++) await Work.Notify(r.Counter,r.Probe,r.Asynchronous,i,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DirectNotification16
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private ZPing0 entry=null!;
    private readonly Counter counter=new(); private Notice16 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Direct"); scope=provider.CreateScope(); entry=new ZPing0(); request=new(counter); ChildEvidence.Record("DirectNotification16", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice16 r,CancellationToken t=default) { for(int i=1;i<=16;i++) await Work.Notify(r.Counter,r.Probe,r.Asynchronous,i,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorClosed
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private ClosedGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(); ChildEvidence.Record("ZendiatorClosed", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(ClosedGeneric<int> r,CancellationToken t=default) { return await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorOpen
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private OpenGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(); ChildEvidence.Record("ZendiatorOpen", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(OpenGeneric<int> r,CancellationToken t=default) { return await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorVoid1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Void1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorVoid1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void1 r,CancellationToken t=default) { await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorVoid3
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Void3 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorVoid3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void3 r,CancellationToken t=default) { await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorVoid5
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Void5 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorVoid5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void5 r,CancellationToken t=default) { await entry.SendAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorNotification0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Notice0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorNotification0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice0 r,CancellationToken t=default) { await entry.PublishAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ZendiatorNotification1
{
    private readonly Notice1 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Notice1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorNotification1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice1 r,CancellationToken t=default) { await entry.PublishAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class ZendiatorNotification4
{
    private readonly Notice4 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Notice4 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorNotification4", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice4 r,CancellationToken t=default) { await entry.PublishAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class ZendiatorNotification16
{
    private readonly Notice16 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private Competitive.Generated.IZendiator entry=null!;
    private readonly Counter counter=new(); private Notice16 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Zendiator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<Competitive.Generated.IZendiator>(); request=new(counter); ChildEvidence.Record("ZendiatorNotification16", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice16 r,CancellationToken t=default) { await entry.PublishAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatRHistoricalClosed
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private ClosedGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(); ChildEvidence.Record("MediatRHistoricalClosed", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(ClosedGeneric<int> r,CancellationToken t=default) { return await entry.Send(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalOpen
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private OpenGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(); ChildEvidence.Record("MediatRHistoricalOpen", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(OpenGeneric<int> r,CancellationToken t=default) { return await entry.Send(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalVoid1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Void1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalVoid1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void1 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalVoid3
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Void3 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalVoid3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void3 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalVoid5
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Void5 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalVoid5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void5 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalNotification0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalNotification0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice0 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatRHistoricalNotification1
{
    private readonly Notice1 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalNotification1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice1 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatRHistoricalNotification4
{
    private readonly Notice4 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice4 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalNotification4", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice4 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatRHistoricalNotification16
{
    private readonly Notice16 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private M.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice16 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("MediatRHistorical"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<M.IMediator>(); request=new(counter); ChildEvidence.Record("MediatRHistoricalNotification16", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice16 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatorVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatorVoid1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Void1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorVoid1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void1 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatorVoid3
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Void3 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorVoid3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void3 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatorVoid5
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Void5 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorVoid5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void5 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatorNotification0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Notice0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorNotification0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice0 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class MediatorNotification1
{
    private readonly Notice1 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Notice1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorNotification1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice1 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatorNotification4
{
    private readonly Notice4 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Notice4 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorNotification4", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice4 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class MediatorNotification16
{
    private readonly Notice16 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private G.Mediator entry=null!;
    private readonly Counter counter=new(); private Notice16 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Mediator"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<G.Mediator>(); request=new(counter); ChildEvidence.Record("MediatorNotification16", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice16 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class DispatchRClosed
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private ClosedGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(); ChildEvidence.Record("DispatchRClosed", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(ClosedGeneric<int> r,CancellationToken t=default) { return await entry.Send(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchROpen
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private OpenGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(); ChildEvidence.Record("DispatchROpen", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(OpenGeneric<int> r,CancellationToken t=default) { return await entry.Send(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRVoid1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Void1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRVoid1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void1 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRVoid3
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Void3 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRVoid3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void3 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRVoid5
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Void5 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRVoid5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void5 r,CancellationToken t=default) { await entry.Send(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRNotification0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRNotification0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice0 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class DispatchRNotification1
{
    private readonly Notice1 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRNotification1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice1 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class DispatchRNotification4
{
    private readonly Notice4 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice4 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRNotification4", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice4 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class DispatchRNotification16
{
    private readonly Notice16 asyncRequest = new(new Counter(), Asynchronous: true);
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private DispatchR.IMediator entry=null!;
    private readonly Counter counter=new(); private Notice16 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("DispatchR"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>(); request=new(counter); ChildEvidence.Record("DispatchRNotification16", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Notice16 r,CancellationToken t=default) { await entry.Publish(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
    [Benchmark] public ValueTask DispatchAsync() => Invoke(asyncRequest);
}
[MemoryDiagnoser] public class ImmediateClosed
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private IClosed.Handler entry=null!;
    private readonly Counter counter=new(); private ClosedGeneric<int> request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IClosed.Handler>(); request=new(); ChildEvidence.Record("ImmediateClosed", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask<int> Invoke(ClosedGeneric<int> r,CancellationToken t=default) { return await entry.HandleAsync(r,t); }
    [Benchmark] public ValueTask<int> Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ImmediateVoid0
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private IVoid0.Handler entry=null!;
    private readonly Counter counter=new(); private Void0 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IVoid0.Handler>(); request=new(counter); ChildEvidence.Record("ImmediateVoid0", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void0 r,CancellationToken t=default) { await entry.HandleAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ImmediateVoid1
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private IVoid1.Handler entry=null!;
    private readonly Counter counter=new(); private Void1 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IVoid1.Handler>(); request=new(counter); ChildEvidence.Record("ImmediateVoid1", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void1 r,CancellationToken t=default) { await entry.HandleAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ImmediateVoid3
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private IVoid3.Handler entry=null!;
    private readonly Counter counter=new(); private Void3 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IVoid3.Handler>(); request=new(counter); ChildEvidence.Record("ImmediateVoid3", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void3 r,CancellationToken t=default) { await entry.HandleAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
[MemoryDiagnoser] public class ImmediateVoid5
{
    private ServiceProvider provider=null!; private IServiceScope scope=null!; private IVoid5.Handler entry=null!;
    private readonly Counter counter=new(); private Void5 request=null!;
    [GlobalSetup] public void Setup() { provider=Registration.Create("Immediate"); scope=provider.CreateScope(); entry=scope.ServiceProvider.GetRequiredService<IVoid5.Handler>(); request=new(counter); ChildEvidence.Record("ImmediateVoid5", "Default"); }
    [GlobalCleanup] public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    public async ValueTask Invoke(Void5 r,CancellationToken t=default) { await entry.HandleAsync(r,t); }
    [Benchmark] public ValueTask Dispatch() => Invoke(request);
}
public static class GeneratedGate { public static async Task Run() {
{
    var send=new DirectSend0(); send.Setup();
    try {
        await Correctness.Send("Direct",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new DirectStream0(); stream.Setup();
    try { await Correctness.Stream("Direct",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ZendiatorSend0(); send.Setup();
    try {
        await Correctness.Send("Zendiator",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ZendiatorStream0(); stream.Setup();
    try { await Correctness.Stream("Zendiator",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ZendiatorSend1(); send.Setup();
    try {
        await Correctness.Send("Zendiator",1, async (p,t)=>await send.Invoke(new Ping1(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ZendiatorStream1(); stream.Setup();
    try { await Correctness.Stream("Zendiator",1,(n,a,p,t)=>stream.Invoke(new Stream1(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ZendiatorSend3(); send.Setup();
    try {
        await Correctness.Send("Zendiator",3, async (p,t)=>await send.Invoke(new Ping3(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ZendiatorStream3(); stream.Setup();
    try { await Correctness.Stream("Zendiator",3,(n,a,p,t)=>stream.Invoke(new Stream3(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ZendiatorSend5(); send.Setup();
    try {
        await Correctness.Send("Zendiator",5, async (p,t)=>await send.Invoke(new Ping5(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ZendiatorStream5(); stream.Setup();
    try { await Correctness.Stream("Zendiator",5,(n,a,p,t)=>stream.Invoke(new Stream5(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatRHistoricalSend0(); send.Setup();
    try {
        await Correctness.Send("MediatRHistorical",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatRHistoricalStream0(); stream.Setup();
    try { await Correctness.Stream("MediatRHistorical",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatRHistoricalSend1(); send.Setup();
    try {
        await Correctness.Send("MediatRHistorical",1, async (p,t)=>await send.Invoke(new Ping1(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatRHistoricalStream1(); stream.Setup();
    try { await Correctness.Stream("MediatRHistorical",1,(n,a,p,t)=>stream.Invoke(new Stream1(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatRHistoricalSend3(); send.Setup();
    try {
        await Correctness.Send("MediatRHistorical",3, async (p,t)=>await send.Invoke(new Ping3(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatRHistoricalStream3(); stream.Setup();
    try { await Correctness.Stream("MediatRHistorical",3,(n,a,p,t)=>stream.Invoke(new Stream3(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatRHistoricalSend5(); send.Setup();
    try {
        await Correctness.Send("MediatRHistorical",5, async (p,t)=>await send.Invoke(new Ping5(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatRHistoricalStream5(); stream.Setup();
    try { await Correctness.Stream("MediatRHistorical",5,(n,a,p,t)=>stream.Invoke(new Stream5(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatorSend0(); send.Setup();
    try {
        await Correctness.Send("Mediator",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatorStream0(); stream.Setup();
    try { await Correctness.Stream("Mediator",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatorSend1(); send.Setup();
    try {
        await Correctness.Send("Mediator",1, async (p,t)=>await send.Invoke(new Ping1(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatorStream1(); stream.Setup();
    try { await Correctness.Stream("Mediator",1,(n,a,p,t)=>stream.Invoke(new Stream1(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatorSend3(); send.Setup();
    try {
        await Correctness.Send("Mediator",3, async (p,t)=>await send.Invoke(new Ping3(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatorStream3(); stream.Setup();
    try { await Correctness.Stream("Mediator",3,(n,a,p,t)=>stream.Invoke(new Stream3(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new MediatorSend5(); send.Setup();
    try {
        await Correctness.Send("Mediator",5, async (p,t)=>await send.Invoke(new Ping5(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new MediatorStream5(); stream.Setup();
    try { await Correctness.Stream("Mediator",5,(n,a,p,t)=>stream.Invoke(new Stream5(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new DispatchRSend0(); send.Setup();
    try {
        await Correctness.Send("DispatchR",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new DispatchRStream0(); stream.Setup();
    try { await Correctness.Stream("DispatchR",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new DispatchRSend1(); send.Setup();
    try {
        await Correctness.Send("DispatchR",1, async (p,t)=>await send.Invoke(new Ping1(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new DispatchRStream1(); stream.Setup();
    try { await Correctness.Stream("DispatchR",1,(n,a,p,t)=>stream.Invoke(new Stream1(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new DispatchRSend3(); send.Setup();
    try {
        await Correctness.Send("DispatchR",3, async (p,t)=>await send.Invoke(new Ping3(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new DispatchRStream3(); stream.Setup();
    try { await Correctness.Stream("DispatchR",3,(n,a,p,t)=>stream.Invoke(new Stream3(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new DispatchRSend5(); send.Setup();
    try {
        await Correctness.Send("DispatchR",5, async (p,t)=>await send.Invoke(new Ping5(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new DispatchRStream5(); stream.Setup();
    try { await Correctness.Stream("DispatchR",5,(n,a,p,t)=>stream.Invoke(new Stream5(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ImmediateSend0(); send.Setup();
    try {
        await Correctness.Send("Immediate",0, async (p,t)=>await send.Invoke(new Ping0(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ImmediateStream0(); stream.Setup();
    try { await Correctness.Stream("Immediate",0,(n,a,p,t)=>stream.Invoke(new Stream0(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ImmediateSend1(); send.Setup();
    try {
        await Correctness.Send("Immediate",1, async (p,t)=>await send.Invoke(new Ping1(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ImmediateStream1(); stream.Setup();
    try { await Correctness.Stream("Immediate",1,(n,a,p,t)=>stream.Invoke(new Stream1(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ImmediateSend3(); send.Setup();
    try {
        await Correctness.Send("Immediate",3, async (p,t)=>await send.Invoke(new Ping3(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ImmediateStream3(); stream.Setup();
    try { await Correctness.Stream("Immediate",3,(n,a,p,t)=>stream.Invoke(new Stream3(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{
    var send=new ImmediateSend5(); send.Setup();
    try {
        await Correctness.Send("Immediate",5, async (p,t)=>await send.Invoke(new Ping5(41,p),t));
        if(await send.ResolveSend()!=42 || await send.ScopeK1()!=42 || await send.ScopeK10()!=420 || await send.ScopeK100()!=4200) throw new InvalidOperationException("Scope result");
    }
    finally { send.Cleanup(); }
    var stream=new ImmediateStream5(); stream.Setup();
    try { await Correctness.Stream("Immediate",5,(n,a,p,t)=>stream.Invoke(new Stream5(n,a,p),t)); }
    finally { stream.Cleanup(); }
}
{ var x=new DirectVoid0(); x.Setup(); try { await Correctness.Void("Direct",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new DirectClosed(); x.Setup(); try { await Correctness.Send("Direct-Closed",0,async(p,t)=>await x.Invoke(new ClosedGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new DirectOpen(); x.Setup(); try { await Correctness.Send("Direct-Open",0,async(p,t)=>await x.Invoke(new OpenGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new DirectNotification0(); x.Setup(); try { await Correctness.Notification("Direct",0,(c,p,a,t)=>x.Invoke(new Notice0(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DirectNotification1(); x.Setup(); try { await Correctness.Notification("Direct",1,(c,p,a,t)=>x.Invoke(new Notice1(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DirectNotification4(); x.Setup(); try { await Correctness.Notification("Direct",4,(c,p,a,t)=>x.Invoke(new Notice4(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DirectNotification16(); x.Setup(); try { await Correctness.Notification("Direct",16,(c,p,a,t)=>x.Invoke(new Notice16(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorVoid0(); x.Setup(); try { await Correctness.Void("Zendiator",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorVoid1(); x.Setup(); try { await Correctness.Void("Zendiator",1,(c,p,t)=>x.Invoke(new Void1(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorVoid3(); x.Setup(); try { await Correctness.Void("Zendiator",3,(c,p,t)=>x.Invoke(new Void3(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorVoid5(); x.Setup(); try { await Correctness.Void("Zendiator",5,(c,p,t)=>x.Invoke(new Void5(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorClosed(); x.Setup(); try { await Correctness.Send("Zendiator-Closed",0,async(p,t)=>await x.Invoke(new ClosedGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorOpen(); x.Setup(); try { await Correctness.Send("Zendiator-Open",0,async(p,t)=>await x.Invoke(new OpenGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorNotification0(); x.Setup(); try { await Correctness.Notification("Zendiator",0,(c,p,a,t)=>x.Invoke(new Notice0(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorNotification1(); x.Setup(); try { await Correctness.Notification("Zendiator",1,(c,p,a,t)=>x.Invoke(new Notice1(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorNotification4(); x.Setup(); try { await Correctness.Notification("Zendiator",4,(c,p,a,t)=>x.Invoke(new Notice4(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorNotification16(); x.Setup(); try { await Correctness.Notification("Zendiator",16,(c,p,a,t)=>x.Invoke(new Notice16(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend0 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedScoped",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend1 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedScoped",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend3 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedScoped",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend5 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedScoped",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend0 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedSingleton",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend1 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedSingleton",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend3 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedSingleton",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ZendiatorSend5 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Zendiator-MatchedSingleton",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
Correctness.Lifetime("Zendiator","Default",typeof(ZPing0));
Correctness.Lifetime("Zendiator","Scoped",typeof(ZPing0));
Correctness.Lifetime("Zendiator","Singleton",typeof(ZPing0));
{ var x=new MediatRHistoricalVoid0(); x.Setup(); try { await Correctness.Void("MediatRHistorical",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalVoid1(); x.Setup(); try { await Correctness.Void("MediatRHistorical",1,(c,p,t)=>x.Invoke(new Void1(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalVoid3(); x.Setup(); try { await Correctness.Void("MediatRHistorical",3,(c,p,t)=>x.Invoke(new Void3(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalVoid5(); x.Setup(); try { await Correctness.Void("MediatRHistorical",5,(c,p,t)=>x.Invoke(new Void5(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalClosed(); x.Setup(); try { await Correctness.Send("MediatRHistorical-Closed",0,async(p,t)=>await x.Invoke(new ClosedGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalOpen(); x.Setup(); try { await Correctness.Send("MediatRHistorical-Open",0,async(p,t)=>await x.Invoke(new OpenGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalNotification0(); x.Setup(); try { await Correctness.Notification("MediatRHistorical",0,(c,p,a,t)=>x.Invoke(new Notice0(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalNotification1(); x.Setup(); try { await Correctness.Notification("MediatRHistorical",1,(c,p,a,t)=>x.Invoke(new Notice1(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalNotification4(); x.Setup(); try { await Correctness.Notification("MediatRHistorical",4,(c,p,a,t)=>x.Invoke(new Notice4(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalNotification16(); x.Setup(); try { await Correctness.Notification("MediatRHistorical",16,(c,p,a,t)=>x.Invoke(new Notice16(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend0 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedScoped",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend1 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedScoped",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend3 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedScoped",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend5 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedScoped",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend0 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedSingleton",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend1 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedSingleton",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend3 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedSingleton",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatRHistoricalSend5 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("MediatRHistorical-MatchedSingleton",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
Correctness.Lifetime("MediatRHistorical","Default",typeof(M.IRequestHandler<Ping0,int>));
Correctness.Lifetime("MediatRHistorical","Scoped",typeof(M.IRequestHandler<Ping0,int>));
Correctness.Lifetime("MediatRHistorical","Singleton",typeof(M.IRequestHandler<Ping0,int>));
{ var x=new MediatorVoid0(); x.Setup(); try { await Correctness.Void("Mediator",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatorVoid1(); x.Setup(); try { await Correctness.Void("Mediator",1,(c,p,t)=>x.Invoke(new Void1(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatorVoid3(); x.Setup(); try { await Correctness.Void("Mediator",3,(c,p,t)=>x.Invoke(new Void3(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatorVoid5(); x.Setup(); try { await Correctness.Void("Mediator",5,(c,p,t)=>x.Invoke(new Void5(c,p),t)); } finally {x.Cleanup();} }
{ var x=new MediatorNotification0(); x.Setup(); try { await Correctness.Notification("Mediator",0,(c,p,a,t)=>x.Invoke(new Notice0(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatorNotification1(); x.Setup(); try { await Correctness.Notification("Mediator",1,(c,p,a,t)=>x.Invoke(new Notice1(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatorNotification4(); x.Setup(); try { await Correctness.Notification("Mediator",4,(c,p,a,t)=>x.Invoke(new Notice4(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new MediatorNotification16(); x.Setup(); try { await Correctness.Notification("Mediator",16,(c,p,a,t)=>x.Invoke(new Notice16(c,p,a),t)); } finally {x.Cleanup();} }
if(Registration.MediatorLifetime == "Scoped") {
{ var x=new MediatorSend0 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedScoped",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend1 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedScoped",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend3 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedScoped",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend5 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedScoped",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
}
if(Registration.MediatorLifetime == "Singleton") {
{ var x=new MediatorSend0 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedSingleton",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend1 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedSingleton",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend3 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedSingleton",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new MediatorSend5 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Mediator-MatchedSingleton",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
}
Correctness.Lifetime("Mediator","Default",typeof(G.IRequestHandler<Ping0,int>));
if(Registration.MediatorLifetime == "Scoped")
Correctness.Lifetime("Mediator","Scoped",typeof(G.IRequestHandler<Ping0,int>));
if(Registration.MediatorLifetime == "Singleton")
Correctness.Lifetime("Mediator","Singleton",typeof(G.IRequestHandler<Ping0,int>));
{ var x=new DispatchRVoid0(); x.Setup(); try { await Correctness.Void("DispatchR",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRVoid1(); x.Setup(); try { await Correctness.Void("DispatchR",1,(c,p,t)=>x.Invoke(new Void1(c,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRVoid3(); x.Setup(); try { await Correctness.Void("DispatchR",3,(c,p,t)=>x.Invoke(new Void3(c,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRVoid5(); x.Setup(); try { await Correctness.Void("DispatchR",5,(c,p,t)=>x.Invoke(new Void5(c,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRClosed(); x.Setup(); try { await Correctness.Send("DispatchR-Closed",0,async(p,t)=>await x.Invoke(new ClosedGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchROpen(); x.Setup(); try { await Correctness.Send("DispatchR-Open",0,async(p,t)=>await x.Invoke(new OpenGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRNotification0(); x.Setup(); try { await Correctness.Notification("DispatchR",0,(c,p,a,t)=>x.Invoke(new Notice0(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRNotification1(); x.Setup(); try { await Correctness.Notification("DispatchR",1,(c,p,a,t)=>x.Invoke(new Notice1(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRNotification4(); x.Setup(); try { await Correctness.Notification("DispatchR",4,(c,p,a,t)=>x.Invoke(new Notice4(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRNotification16(); x.Setup(); try { await Correctness.Notification("DispatchR",16,(c,p,a,t)=>x.Invoke(new Notice16(c,p,a),t)); } finally {x.Cleanup();} }
{ var x=new DispatchRSend0 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("DispatchR-MatchedScoped",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new DispatchRSend1 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("DispatchR-MatchedScoped",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new DispatchRSend3 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("DispatchR-MatchedScoped",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new DispatchRSend5 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("DispatchR-MatchedScoped",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
Correctness.Lifetime("DispatchR","Default",typeof(D.IRequestHandler<Ping0,ValueTask<int>>));
Correctness.Lifetime("DispatchR","Scoped",typeof(D.IRequestHandler<Ping0,ValueTask<int>>));
{ var x=new ImmediateVoid0(); x.Setup(); try { await Correctness.Void("Immediate",0,(c,p,t)=>x.Invoke(new Void0(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ImmediateVoid1(); x.Setup(); try { await Correctness.Void("Immediate",1,(c,p,t)=>x.Invoke(new Void1(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ImmediateVoid3(); x.Setup(); try { await Correctness.Void("Immediate",3,(c,p,t)=>x.Invoke(new Void3(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ImmediateVoid5(); x.Setup(); try { await Correctness.Void("Immediate",5,(c,p,t)=>x.Invoke(new Void5(c,p),t)); } finally {x.Cleanup();} }
{ var x=new ImmediateClosed(); x.Setup(); try { await Correctness.Send("Immediate-Closed",0,async(p,t)=>await x.Invoke(new ClosedGeneric<int>(41,p),t)); } finally {x.Cleanup();} }
{ var x=new ImmediateSend0 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedScoped",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend1 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedScoped",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend3 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedScoped",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend5 {Lifetime="Scoped"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedScoped",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend0 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedSingleton",0,async(p,t)=>await x.Invoke(new Ping0(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend1 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedSingleton",1,async(p,t)=>await x.Invoke(new Ping1(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend3 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedSingleton",3,async(p,t)=>await x.Invoke(new Ping3(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
{ var x=new ImmediateSend5 {Lifetime="Singleton"}; x.Setup(); try { await Correctness.Send("Immediate-MatchedSingleton",5,async(p,t)=>await x.Invoke(new Ping5(41,p),t)); if(await x.ScopeK1()!=42 || await x.ResolveSend()!=42) throw new InvalidOperationException("Scope result"); } finally {x.Cleanup();} }
Correctness.Lifetime("Immediate","Default",typeof(IPing0.Handler));
Correctness.Lifetime("Immediate","Scoped",typeof(IPing0.Handler));
Correctness.Lifetime("Immediate","Singleton",typeof(IPing0.Handler));
} }
