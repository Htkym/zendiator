using global::Zendiator;

namespace Zendiator.Tests;

public sealed record DeleteEntities<T>(int[] Ids) : ICommand
    where T : class;
