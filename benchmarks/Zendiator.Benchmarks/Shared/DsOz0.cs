namespace Zendiator.Benchmarks;

// DSoft open behavior must stay in the isolated assembly (it would apply to DsP0/DsPing here).

public sealed record DsOz0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;
