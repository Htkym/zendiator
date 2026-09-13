namespace Zendiator.Benchmarks;

// 16-subscriber fan-out sets, one handler per class.

public sealed record MrEv16(int Value) : global::MediatR.INotification;
