using global::Zendiator;

namespace Zendiator.Tests;

public sealed record ChangeToken(CancellationToken Replacement) : IRequest<CancellationToken>;
