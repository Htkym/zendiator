using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Fail(Exception Error) : IRequest<int>;
