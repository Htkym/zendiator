using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

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
