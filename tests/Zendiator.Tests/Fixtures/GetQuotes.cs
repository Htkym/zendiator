using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>;
