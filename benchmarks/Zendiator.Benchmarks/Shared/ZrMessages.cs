using Zendiator;

namespace Zendiator.Benchmarks;

// Struct echo-plus-one handlers; markers select pipeline depth.

public interface IZrMarker1;
public interface IZrMarker2;
public interface IZrMarker3;
public interface IZrMarker4;
public interface IZrMarker5;

public readonly record struct Zp0(int Value) : IRequest<int>;
public readonly record struct Zp1(int Value) : IRequest<int>, IZrMarker1;
public readonly record struct Zp3(int Value) : IRequest<int>, IZrMarker1, IZrMarker2, IZrMarker3;
public readonly record struct Zp5(int Value) : IRequest<int>, IZrMarker1, IZrMarker2, IZrMarker3, IZrMarker4, IZrMarker5;

public sealed class Zp0Handler : IRequestHandler<Zp0, int>
{
    public ValueTask<int> HandleAsync(Zp0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class Zp1Handler : IRequestHandler<Zp1, int>
{
    public ValueTask<int> HandleAsync(Zp1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class Zp3Handler : IRequestHandler<Zp3, int>
{
    public ValueTask<int> HandleAsync(Zp3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class Zp5Handler : IRequestHandler<Zp5, int>
{
    public ValueTask<int> HandleAsync(Zp5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

/// <summary>Invocation counters. Plain increments: benchmarks stay single-threaded,
/// so no fence is needed and the cost stays below a nanosecond.</summary>
public static class ZrCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = 0;
}

public sealed class Zb1<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker1
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B1++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class Zb2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker2
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B2++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class Zb3<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker3
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B3++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class Zb4<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker4
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B4++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class Zb5<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker5
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B5++;
        return next.InvokeAsync(request, cancellationToken);
    }
}

public readonly record struct ZpAsync(int Value) : IRequest<int>;

public sealed class ZpAsyncHandler : IRequestHandler<ZpAsync, int>
{
    public async ValueTask<int> HandleAsync(ZpAsync request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}
