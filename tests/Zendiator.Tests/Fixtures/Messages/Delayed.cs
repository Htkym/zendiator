using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Delayed(Task<int> Pending) : IRequest<int>;
