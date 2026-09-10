using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Echo(string? Value) : IRequest<string?>;
