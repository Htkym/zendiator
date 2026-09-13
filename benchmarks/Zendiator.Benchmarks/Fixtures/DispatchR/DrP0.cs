namespace Zendiator.Benchmarks;

public sealed record DrP0(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP0, ValueTask<int>>;
