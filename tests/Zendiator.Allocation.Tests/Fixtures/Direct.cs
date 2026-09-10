using Zendiator;

namespace Zendiator.Allocation.Tests;

/// <summary>Zero-allocation request with no matching behavior.</summary>
public readonly record struct Direct(int Value) : IRequest<int>;
