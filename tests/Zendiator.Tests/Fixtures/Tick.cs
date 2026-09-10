using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct Tick(int N) : INotification;
