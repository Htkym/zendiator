using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct GetLabel(int Id) : ISyncRequest<string>;
