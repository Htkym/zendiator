using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Marks the application assembly for compile-time discovery.</summary>
public sealed class ApplicationAssemblyMarker;

/// <summary>Returns canned memorial targets. No allocation on the synchronous path.</summary>
public sealed class GetTargetYearQueryHandler : IQueryHandler<GetTargetYearQuery, MemorialTargetDto>
{
    public ValueTask<MemorialTargetDto> HandleAsync(GetTargetYearQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new MemorialTargetDto(query.Year, [$"Household-{query.Year}-1", $"Household-{query.Year}-2"]));
    }
}

/// <summary>Validates the command and returns a failure instead of throwing.</summary>
public sealed class RegisterHouseholdHandler : ICommandHandler<RegisterHouseholdCommand, Result<HouseholdId, ValidationError>>
{
    public ValueTask<Result<HouseholdId, ValidationError>> HandleAsync(
        RegisterHouseholdCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.Name))
            return new(Result<HouseholdId, ValidationError>.Failure(new ValidationError("Name", "Name must not be empty.")));

        return new(Result<HouseholdId, ValidationError>.Success(new HouseholdId(Guid.NewGuid())));
    }
}

/// <summary>Open logging behavior applied to every request. Resolved from DI per scope.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="logger">The logger.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        }
    }
}

/// <summary>Logging behavior for commands without a response.</summary>
public sealed class CommandLoggingBehavior<TRequest>(ILogger<CommandLoggingBehavior<TRequest>> logger)
    : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public async ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        try
        {
            await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        }
    }
}

/// <summary>Purges a household. No <c>Unit</c> appears in user code.</summary>
public sealed class PurgeHouseholdHandler(ILogger<PurgeHouseholdHandler> logger) : IRequestHandler<PurgeHouseholdCommand>
{
    public ValueTask HandleAsync(PurgeHouseholdCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Purged {Id}", command.Id);
        return default;
    }
}

/// <summary>Counts households for any marker type.</summary>
public sealed class GetHouseholdCountHandler<TMarker> : IRequestHandler<GetHouseholdCount<TMarker>, int>
{
    public ValueTask<int> HandleAsync(GetHouseholdCount<TMarker> request, CancellationToken cancellationToken) => new(42);
}

/// <summary>Audits purges after cache invalidation (see <see cref="InvalidatePurgeCacheHandler"/>).</summary>
public sealed class AuditPurgeHandler(ILogger<AuditPurgeHandler> logger) : INotificationHandler<HouseholdPurged>
{
    public ValueTask HandleAsync(HouseholdPurged notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Audited purge {Id}", notification.Id);
        return default;
    }
}

/// <summary>Invalidates caches first when a household is purged.</summary>
[HandlerOrder(Order = -1)]
public sealed class InvalidatePurgeCacheHandler(ILogger<InvalidatePurgeCacheHandler> logger) : INotificationHandler<HouseholdPurged>
{
    public ValueTask HandleAsync(HouseholdPurged notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidated purge cache {Id}", notification.Id);
        return default;
    }
}

/// <summary>Quotes from vendor A.</summary>
public sealed class VendorAQuotesHandler : IRequestHandler<GetHouseholdQuotes, HouseholdQuote>
{
    public ValueTask<HouseholdQuote> HandleAsync(GetHouseholdQuotes request, CancellationToken cancellationToken) =>
        new(new HouseholdQuote("A", 100m));
}

/// <summary>Quotes from vendor B.</summary>
[HandlerOrder(Order = 1)]
public sealed class VendorBQuotesHandler : IRequestHandler<GetHouseholdQuotes, HouseholdQuote>
{
    public ValueTask<HouseholdQuote> HandleAsync(GetHouseholdQuotes request, CancellationToken cancellationToken) =>
        new(new HouseholdQuote("B", 200m));
}

/// <summary>Parses a four-digit year from UTF-8 bytes on the calling stack.</summary>
public sealed class ParseYearHandler : ISyncRequestHandler<ParseYear, int>
{
    public int Handle(scoped ParseYear request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        return (data[0] - (byte)'0') * 1000 + (data[1] - (byte)'0') * 100 + (data[2] - (byte)'0') * 10 + (data[3] - (byte)'0');
    }
}

/// <summary>Resets cached state without a response.</summary>
public sealed class ResetCacheHandler(ILogger<ResetCacheHandler> logger) : ISyncRequestHandler<ResetCache>
{
    public void Handle(ResetCache request, CancellationToken cancellationToken) =>
        logger.LogInformation("Reset cache");
}

/// <summary>Legacy Unit-style handler kept for compatibility.</summary>
public sealed class LegacyPingHandler : ICommandHandler<LegacyPing>
{
    public ValueTask<Unit> HandleAsync(LegacyPing request, CancellationToken cancellationToken) => new(Unit.Value);
}

/// <summary>Streams household names lazily; enumeration starts only on first MoveNext.</summary>
public sealed class GetHouseholdNamesHandler : IStreamRequestHandler<GetHouseholdNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GetHouseholdNames request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return $"Household-{i}";
        }
    }
}

/// <summary>Logging behavior for streams. Runs once per enumeration, not per item.</summary>
public sealed class StreamLoggingBehavior<TRequest, TItem>(ILogger<StreamLoggingBehavior<TRequest, TItem>> logger)
    : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>
    {
        logger.LogInformation("Streaming {Request}", typeof(TRequest).Name);
        try
        {
            await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            logger.LogInformation("Streamed {Request}", typeof(TRequest).Name);
        }
    }
}
