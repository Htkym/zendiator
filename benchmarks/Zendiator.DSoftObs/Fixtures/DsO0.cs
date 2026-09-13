namespace Zendiator.DSoftObs;

// Isolated open post-only behavior (DsO0(41) = 141).

public sealed record DsO0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;
