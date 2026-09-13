namespace Zendiator.Benchmarks;

public sealed record DrO5(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO5, ValueTask<int>>;
