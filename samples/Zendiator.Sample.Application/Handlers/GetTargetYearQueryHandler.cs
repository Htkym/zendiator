using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Returns canned memorial targets. No allocation on the synchronous path.</summary>
public sealed class GetTargetYearQueryHandler : IQueryHandler<GetTargetYearQuery, MemorialTargetDto>
{
    public ValueTask<MemorialTargetDto> HandleAsync(GetTargetYearQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new MemorialTargetDto(query.Year, [$"Household-{query.Year}-1", $"Household-{query.Year}-2"]));
    }
}
