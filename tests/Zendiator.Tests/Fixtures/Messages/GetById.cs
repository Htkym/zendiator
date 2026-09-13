using global::Zendiator;

namespace Zendiator.Tests;

public sealed record GetById<T>(int Id) : IRequest<T>
    where T : class;
