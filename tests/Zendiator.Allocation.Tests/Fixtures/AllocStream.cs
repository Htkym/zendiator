using Zendiator;

namespace Zendiator.Allocation.Tests;

public sealed record AllocStream(int Count) : IStreamRequest<int>;
