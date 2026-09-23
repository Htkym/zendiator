using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.PipeDepth;

/// <summary>In-memory order pricing with validation, tenant authorization and auditing.</summary>
[MemoryDiagnoser]
public class OrderScenarioBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IZendiator _mediator = null!;
    private CancellationTokenSource _cancellation = null!;
    private Order _order = new(7, 2, 3);

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderState>();
        services.AddZendiator();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _mediator = _scope.ServiceProvider.GetRequiredService<IZendiator>();
        _cancellation = new CancellationTokenSource();
        var state = _scope.ServiceProvider.GetRequiredService<OrderState>();
        if (WarmOrder().GetAwaiter().GetResult() != 375
            || WarmCancelableOrder().GetAwaiter().GetResult() != 375
            || state.AcceptedOrders != 2 || ScopeOrderBatch16() != 6000)
            throw new InvalidOperationException("Order result or audit mismatch.");
        try
        {
            _mediator.SendAsync(_order with { Quantity = 0 }).GetAwaiter().GetResult();
            throw new InvalidOperationException("Invalid quantity was accepted.");
        }
        catch (ArgumentOutOfRangeException) { }
        try
        {
            _mediator.SendAsync(_order with { TenantId = 8 }).GetAwaiter().GetResult();
            throw new InvalidOperationException("Another tenant was accepted.");
        }
        catch (UnauthorizedAccessException) { }
        if (state.AcceptedOrders != 2)
            throw new InvalidOperationException("Rejected orders were audited as accepted.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cancellation.Dispose();
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark]
    public ValueTask<int> WarmOrder() => _mediator.SendAsync(_order);

    [Benchmark]
    public ValueTask<int> WarmCancelableOrder() => _mediator.SendAsync(_order, _cancellation.Token);

    [Benchmark]
    public int ScopeOrderBatch16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var total = 0;
        for (var i = 0; i < 16; i++)
            total += mediator.SendAsync(_order, _cancellation.Token).GetAwaiter().GetResult();
        return total;
    }

    public readonly record struct Order(int TenantId, int ProductId, int Quantity) : IRequest<int>;

    public sealed class OrderState
    {
        public int TenantId { get; } = 7;
        public int MaximumQuantity { get; } = 100;
        public Dictionary<int, int> Prices { get; } = new() { [1] = 250, [2] = 125, [3] = 500 };
        public long AcceptedOrders;
    }

    public sealed class Validation(OrderState state) : IPipelineBehavior<Order, int>
    {
        public ValueTask<int> HandleAsync<TNext>(Order request, TNext next, CancellationToken cancellationToken)
            where TNext : struct, IRequestContinuation<Order, int>
        {
            if (request.Quantity <= 0 || request.Quantity > state.MaximumQuantity)
                throw new ArgumentOutOfRangeException(nameof(request.Quantity));
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    public sealed class Authorization(OrderState state) : IPipelineBehavior<Order, int>
    {
        public ValueTask<int> HandleAsync<TNext>(Order request, TNext next, CancellationToken cancellationToken)
            where TNext : struct, IRequestContinuation<Order, int>
        {
            if (request.TenantId != state.TenantId)
                throw new UnauthorizedAccessException();
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    public sealed class Audit(OrderState state) : IPipelineBehavior<Order, int>
    {
        public ValueTask<int> HandleAsync<TNext>(Order request, TNext next, CancellationToken cancellationToken)
            where TNext : struct, IRequestContinuation<Order, int>
        {
            Interlocked.Increment(ref state.AcceptedOrders);
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    public sealed class Pricing(OrderState state) : IRequestHandler<Order, int>
    {
        public ValueTask<int> HandleAsync(Order request, CancellationToken cancellationToken) =>
            new(checked(state.Prices[request.ProductId] * request.Quantity));
    }
}

[PipelineBehavior(typeof(OrderScenarioBenchmarks.Validation), Order = 100)]
[PipelineBehavior(typeof(OrderScenarioBenchmarks.Authorization), Order = 101)]
[PipelineBehavior(typeof(OrderScenarioBenchmarks.Audit), Order = 102)]
public sealed partial class Zendiator;
