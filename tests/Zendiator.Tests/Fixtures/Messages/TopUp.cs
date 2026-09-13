using global::Zendiator;

namespace Zendiator.Tests;

public sealed record TopUp(int UserId) : ICommand;
