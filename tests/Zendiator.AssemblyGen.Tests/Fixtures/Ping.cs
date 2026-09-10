using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public readonly record struct Ping(int Value) : IRequest<int>;
