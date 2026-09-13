namespace Zendiator.Benchmarks;

public sealed record DrP5(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP5, ValueTask<int>>;
