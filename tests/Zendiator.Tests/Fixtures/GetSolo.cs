using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetSolo(int Id) : IMultiRequest<int>;
