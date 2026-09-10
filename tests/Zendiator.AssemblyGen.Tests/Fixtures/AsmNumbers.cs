using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed record AsmNumbers(int Count) : IStreamRequest<int>;
