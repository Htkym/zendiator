using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Enabled_DI_Hook_Handler_session_public_registration_preserves_DI_and_session_owners(bool owned, bool isVoid)
    {
        var fixture = HookHandlerBackendFixture(owned, isVoid);
        var probe = new HookHandlerSessionProbe();
        var core = probe.Core;
        var ambient = ReferenceSendProbe.Ambient.Value;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(probe);
            services.AddScoped<HookHandlerFactoryDependency>();
            Assert.Same(services, fixture.Register.Invoke(null, [services]));
            var hook = fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
            var handler = fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
            Assert.DoesNotContain(services, d => d.ServiceType == handler);
            Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == hook).Lifetime);
            Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == fixture.Service).Lifetime);
            var golden = new List<string>();
            var snapshots = new List<object>();
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var mediator = scope.ServiceProvider.GetRequiredService(fixture.Interface);
                        for (var i = 0; i < 2; i++)
                        {
                            var request = Activator.CreateInstance(fixture.Request)!;
                            var token = core.Cancellation.Token;
                            var actual = InvokeSync(fixture, mediator, request, token);
                            Assert.Null(actual.Error);
                            Assert.Equal(isVoid ? null : (object)42, actual.Result);
                            if (i == 0)
                                golden.Add("hook.ctor");
                            golden.Add("before");
                            if (i == 0)
                                golden.AddRange(["dependency.ctor", "factory.ctor"]);
                            golden.AddRange(["factory", "handler.ctor", "handler"]);
                            if (owned)
                                golden.Add("handler.dispose");
                            golden.AddRange(["after", "finally"]);
                            Assert.Equal(golden, core.Phases);
                            Assert.Equal(s * 2 + i + 1, probe.Created.Count);
                            Assert.Equal(owned ? probe.Created.Count : 0, probe.Released.Count);
                            Assert.Equal(s, core.HookDisposals);
                            Assert.Equal(s, probe.FactoryDisposals);
                            Assert.Equal(s, probe.DependencyDisposals);
                            var callbacks = core.Calls.TakeLast(4).ToArray();
                            Assert.Equal(new[] { "before", "handler", "after", "finally" }, callbacks.Select(c => c.Phase));
                            Assert.All(callbacks, c =>
                            {
                                Assert.Same(request, c.Request);
                                Assert.Equal(token, c.Token);
                            });
                            var inner = probe.Calls.TakeLast(owned ? 2 : 1).ToArray();
                            Assert.Equal(owned ? new[] { "factory", "handler.dispose" } : new[] { "factory" }, inner.Select(c => c.Phase));
                            Assert.All(inner, c =>
                            {
                                Assert.Same(request, c.Request);
                                Assert.Equal(token, c.Token);
                            });
                            snapshots.Add(new
                            {
                                scope = s,
                                send = i,
                                trace = core.Phases.ToArray(),
                                created = probe.Created.Count,
                                operationReleased = probe.Released.Count,
                                callbackPhases = callbacks.Select(c => c.Phase).ToArray(),
                                innerPhases = inner.Select(c => c.Phase).ToArray(),
                                core.HookDisposals,
                                probe.FactoryDisposals,
                                probe.DependencyDisposals
                            });
                        }
                    }
                    golden.AddRange(["factory.dispose", "dependency.dispose"]);
                    Assert.Equal(golden, core.Phases);
                    Assert.Equal(s + 1, core.HookDisposals);
                    Assert.Equal(s + 1, probe.FactoryDisposals);
                    Assert.Equal(s + 1, probe.DependencyDisposals);
                }
                Assert.Equal(4, probe.Created.Count);
                Assert.Equal(owned ? 4 : 0, probe.Released.Count);
            }
            Assert.Equal(2, core.HookDisposals);
            Assert.Equal(2, probe.FactoryDisposals);
            Assert.Equal(2, probe.DependencyDisposals);
            foreach (var session in probe.Borrowed)
                session.Dispose();
            Assert.Equal(4, probe.Released.Count);
            Assert.Equal(4, probe.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.All(probe.Created, session => Assert.Single(probe.Released, item => ReferenceEquals(item, session)));
            WriteNestedHookEvidence("hook-handler-session-api", owned + "-" + isVoid, new
            {
                owned,
                isVoid,
                snapshots,
                createdSessions = 4,
                operationReleasedSessions = owned ? 4 : 0,
                externalOwnerReleasedSessions = owned ? 0 : 4,
                finalOwnerReleasedSessions = probe.Released.Count,
                core.HookDisposals,
                probe.FactoryDisposals,
                probe.DependencyDisposals
            });
        }
        finally
        {
            core.Cancellation.Dispose();
            ReferenceSendProbe.Ambient.Value = ambient;
            CultureInfo.CurrentCulture = culture;
        }
    }
}
