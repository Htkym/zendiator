namespace Zendiator.Benchmarks;

public sealed record DrO3(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO3, ValueTask<int>>;
