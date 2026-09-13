using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct ResetSync(string Name) : ISyncMultiRequest;
