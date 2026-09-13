using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed record GetById<T>(int Id) : IRequest<T>
    where T : class;
