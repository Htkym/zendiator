using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Closed_capture_preserves_alias_types_and_factory_DI_ownership()
    {
        var source = "using OuterAlias=App.Hook<App.Outer>;using InnerAlias=App.Hook<App.Inner>;using HandlerAlias=App.Handler<string>;\n" +
            NestedHookBackendSource(false)
                .Replace("Stage.Hook<Hook<Outer>>()", "Stage.Hook<OuterAlias>()")
                .Replace("Stage.Hook<Hook<Inner>>()", "Stage.Hook<InnerAlias>()")
                .Replace("typeof(Handler<string>)", "typeof(HandlerAlias)");
        var fixture = CompileSyncFixture("direct", false, sourceOverride: source);
        var types = NestedHookFixtureTypes(fixture);
        var probe = new NestedSendProbe();
        var ambient = NestedSendProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(probe);
            services.Add(ServiceDescriptor.Transient(types[0], types[0]));
            services.Add(ServiceDescriptor.Scoped(types[0], types[0]));
            services.Add(ServiceDescriptor.Scoped(types[1], types[1]));
            services.Add(ServiceDescriptor.Scoped(types[1], servicesProvider => ActivatorUtilities.CreateInstance(servicesProvider, types[1])));
            services.Add(ServiceDescriptor.KeyedSingleton(types[0], "unused", (_, _) => throw new InvalidOperationException("Unused keyed factory.")));
            fixture.Register.Invoke(null, [services]);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using (var scope = provider.CreateScope())
            {
                var tracked = new SharedOpenLookupProvider(scope.ServiceProvider);
                var mediator = Activator.CreateInstance(fixture.Mediator, [tracked])!;
                Assert.Empty(probe.Constructed);
                for (var i = 0; i < 2; i++)
                {
                    probe.Exits.Clear();
                    var request = Activator.CreateInstance(fixture.Request, ["request-" + i])!;
                    var result = InvokeSync(fixture, mediator, request, default);
                    Assert.Null(result.Error);
                    Assert.Equal(43, result.Result);
                }
                Assert.Equal(1, tracked.Lookups[types[0]]);
                Assert.Equal(1, tracked.Lookups[types[1]]);
                Assert.Equal(1, tracked.Lookups[types[2]]);
                Assert.Empty(probe.Disposals);
            }
            Assert.Equal(1, probe.Disposals["A"]);
            Assert.Equal(1, probe.Disposals["B"]);
            Assert.Equal(1, probe.Disposals["handler"]);
        }
        finally
        {
            NestedSendProbe.Ambient.Value = ambient;
            CultureInfo.CurrentCulture = culture;
            probe.Cancellation.Dispose();
        }
    }
}
