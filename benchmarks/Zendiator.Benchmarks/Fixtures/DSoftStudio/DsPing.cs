namespace Zendiator.Benchmarks;

// ---------- DSoftStudio.Mediator (MediatR-style, ValueTask) ----------

public sealed record DsPing(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;
