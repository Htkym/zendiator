using global::Zendiator;

namespace Zendiator.Tests;

public sealed record Fragile(int Id) : INotification;
