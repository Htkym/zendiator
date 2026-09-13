namespace Zendiator.Benchmarks;

// ---------- Cross-lane observable: post-only +100 per layer ----------
// Handler echoes Value; O0(v)=v, O1=v+100, O3=v+300, O5=v+500.

public sealed record DrO0(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO0, ValueTask<int>>;
