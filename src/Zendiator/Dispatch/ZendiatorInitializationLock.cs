namespace Zendiator.DependencyInjection;

// Keep the provider in the private monitor object so it needs no separate resolver field.
internal sealed class ZendiatorInitializationLock(IServiceProvider provider)
{
    internal readonly IServiceProvider Provider = provider;
}
