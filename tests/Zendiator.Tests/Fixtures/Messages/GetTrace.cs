using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetTrace(TraceLog Log) : IMultiRequest<string>;
