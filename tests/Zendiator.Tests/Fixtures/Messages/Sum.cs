using global::Zendiator;

namespace Zendiator.Tests;

public readonly record struct Sum(int Left, int Right) : IQuery<int>;
