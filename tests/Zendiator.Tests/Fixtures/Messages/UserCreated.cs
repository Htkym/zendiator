using global::Zendiator;

namespace Zendiator.Tests;

public sealed record UserCreated(int UserId) : INotification;
