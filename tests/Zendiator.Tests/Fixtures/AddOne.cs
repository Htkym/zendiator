using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct AddOne(int Value) : ISyncRequest<int>;
