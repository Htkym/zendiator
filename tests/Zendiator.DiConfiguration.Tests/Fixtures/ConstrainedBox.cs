using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public readonly ref struct ConstrainedBox<T> : ISyncRequest<int> where T : struct, allows ref struct;
