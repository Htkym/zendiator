using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed record Wipe(int Value) : ICommand;
