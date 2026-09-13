namespace Zendiator.Benchmarks;

public sealed record DrO1(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO1, ValueTask<int>>;
