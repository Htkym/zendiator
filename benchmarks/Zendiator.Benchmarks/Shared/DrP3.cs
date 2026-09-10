namespace Zendiator.Benchmarks;

public sealed record DrP3(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP3, ValueTask<int>>;
