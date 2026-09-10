namespace Zendiator.Benchmarks;

public sealed record DrP1(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP1, ValueTask<int>>;
