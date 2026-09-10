using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GuardedDelete(int UserId) : ICommand;
