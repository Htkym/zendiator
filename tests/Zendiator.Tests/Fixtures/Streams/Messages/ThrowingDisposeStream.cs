using global::Zendiator;

namespace Zendiator.Tests;

public sealed record ThrowingDisposeStream(
    Func<ValueTask>? DisposeCallback = null,
    bool FailOnStart = false,
    bool FailOnGetEnumerator = false,
    Func<ValueTask<bool>>? MoveNextCallback = null) : IStreamRequest<int>;
