namespace Zendiator.Benchmarks;

// ---------- DispatchR (class-only requests, ValueTask) ----------

public sealed record DrPing(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrPing, ValueTask<int>>;
