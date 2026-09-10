using Zendiator;

namespace Zendiator.Allocation.Tests;

/// <summary>Zero-allocation request with one pass-through behavior.</summary>
public readonly record struct Ping(int Value) : IRequest<int>;
