using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct Validate(bool Valid) : ICommand<Outcome>;
