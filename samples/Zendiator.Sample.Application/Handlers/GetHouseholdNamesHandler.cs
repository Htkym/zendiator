using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Opens a cursor; asynchronous item production starts on its first move.</summary>
[ZendiatorHandler]
public sealed class GetHouseholdNamesHandler : IStreamSourceHandler<GetHouseholdNames, string, HouseholdNameCursor>
{
    public ValueTask<HouseholdNameCursor> OpenAsync(GetHouseholdNames request, CancellationToken cancellationToken) =>
        new(new HouseholdNameCursor(request.Count, cancellationToken));
}

public sealed class HouseholdNameCursor(int count, CancellationToken cancellationToken) : IAsyncEnumerator<string>
{
    private int _index;
    public string Current { get; private set; } = null!;

    public async ValueTask<bool> MoveNextAsync()
    {
        if (_index >= count) return false;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Current = $"Household-{_index++}";
        return true;
    }

    public ValueTask DisposeAsync() { _index = count; return default; }
}
