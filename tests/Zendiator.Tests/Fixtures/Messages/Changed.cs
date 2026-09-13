using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Changed<T>(T Value) : INotification;
