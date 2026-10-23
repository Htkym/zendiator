using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Enabled_mixed_sessions_use_standard_registration_and_preserve_operation_ownership(bool hookOwned, bool handlerOwned, bool isVoid)
    {
        var fixture = MixedBackendFixture(hookOwned, handlerOwned, isVoid); var mixed = new MixedSessionProbe(); var probe = mixed.Handler; var core = mixed.Core;
        var ambient = ReferenceSendProbe.Ambient.Value; var culture = CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(mixed); services.AddSingleton(probe);
            services.AddScoped<MixedHookDependency>(); services.AddScoped<HookHandlerFactoryDependency>();
            Assert.Same(services, fixture.Register.Invoke(null, [services]));
            var types = MixedSessionTypes(hookOwned, handlerOwned, isVoid, fixture);
            Assert.DoesNotContain(services, d => d.ServiceType == types.Hook || d.ServiceType == types.Handler);
            Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == types.HookFactory).Lifetime);
            Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == types.HandlerFactory).Lifetime);
            var golden = new List<string>(); var snapshots = new List<object>();
            using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                for (var s = 0; s < 2; s++)
                {
                    using (var scope = provider.CreateScope()) for (var i = 0; i < 2; i++)
                    {
                        var fail = i == 1; var body = new Exception("API body identity"); var inner = new Exception("API Handler cleanup identity"); var outer = new Exception("API Hook cleanup identity");
                        core.HandlerError = fail ? body : null; probe.CleanupError = fail && handlerOwned ? inner : null; mixed.HookCleanupError = fail && hookOwned ? outer : null;
                        var request = Activator.CreateInstance(fixture.Request)!; var token = core.Cancellation.Token;
                        var actual = InvokeSync(fixture, scope.ServiceProvider.GetRequiredService(fixture.Interface), request, token);
                        if (i == 0) golden.AddRange(["hook-dependency.ctor", "hook-factory.ctor"]); golden.AddRange(["hook.factory", "hook.ctor", "before"]);
                        if (i == 0) golden.AddRange(["dependency.ctor", "factory.ctor"]); golden.AddRange(["factory", "handler.ctor", "handler"]);
                        if (handlerOwned) golden.Add("handler.dispose"); golden.AddRange(fail ? ["onexception", "finally"] : ["after", "finally"]); if (hookOwned) golden.Add("hook.dispose");
                        Assert.Equal(golden, core.Phases);
                        var exit = Assert.IsType<SendExitInfo>(core.Exit);
                        if (fail)
                        {
                            var observed = handlerOwned ? inner : body; var escaping = hookOwned ? outer : observed;
                            Assert.Same(escaping, actual.Error); Assert.Same(observed, core.ObservedError); Assert.False(exit.Success); Assert.Same(observed, exit.Error);
                        }
                        else { Assert.Null(actual.Error); Assert.Equal(isVoid ? null : (object)42, actual.Result); Assert.True(exit.Success); Assert.Null(exit.Error); }
                        var created = s * 2 + i + 1; Assert.Equal(created, mixed.CreatedHooks.Count); Assert.Equal(created, probe.Created.Count);
                        Assert.Equal(hookOwned ? created : 0, mixed.ReleasedHooks.Count); Assert.Equal(handlerOwned ? created : 0, probe.Released.Count);
                        Assert.Equal(s, mixed.HookFactoryDisposals); Assert.Equal(s, mixed.HookDependencyDisposals); Assert.Equal(s, probe.FactoryDisposals); Assert.Equal(s, probe.DependencyDisposals);
                        var callbacks = core.Calls.TakeLast(4).ToArray(); Assert.Equal(fail ? new[] { "before", "handler", "onexception", "finally" } : new[] { "before", "handler", "after", "finally" }, callbacks.Select(c => c.Phase));
                        Assert.All(callbacks, c => { Assert.Same(request, c.Request); Assert.Equal(token, c.Token); });
                        snapshots.Add(new
                        {
                            scope = s,
                            send = i,
                            fail,
                            createdHooks = created,
                            createdHandlers = created,
                            operationHookReleases = mixed.ReleasedHooks.Count,
                            operationHandlerReleases = probe.Released.Count,
                            trace = core.Phases.ToArray(),
                            escapingIdentityPreserved = !fail || ReferenceEquals(hookOwned ? outer : handlerOwned ? inner : body, actual.Error),
                            observerIdentityPreserved = !fail || ReferenceEquals(handlerOwned ? inner : body, core.ObservedError),
                            exitIdentityPreserved = !fail || ReferenceEquals(handlerOwned ? inner : body, core.Exit?.Error)
                        });
                    }
                    golden.AddRange(["factory.dispose", "dependency.dispose", "hook-factory.dispose", "hook-dependency.dispose"]); Assert.Equal(golden, core.Phases);
                    Assert.Equal(s + 1, mixed.HookFactoryDisposals); Assert.Equal(s + 1, mixed.HookDependencyDisposals); Assert.Equal(s + 1, probe.FactoryDisposals); Assert.Equal(s + 1, probe.DependencyDisposals);
                }
            }
            foreach (var handler in probe.Borrowed) handler.Dispose(); foreach (var hook in mixed.BorrowedHooks) hook.Dispose();
            Assert.Equal(4, mixed.CreatedHooks.Distinct(ReferenceEqualityComparer.Instance).Count()); Assert.Equal(4, probe.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.All(mixed.CreatedHooks, h => Assert.Single(mixed.ReleasedHooks, r => ReferenceEquals(h, r))); Assert.All(probe.Created, h => Assert.Single(probe.Released, r => ReferenceEquals(h, r)));
            WriteNestedHookEvidence("mixed-session-api", hookOwned + "-" + handlerOwned + "-" + isVoid, new
            {
                hookOwned,
                handlerOwned,
                isVoid,
                snapshots,
                createdHooks = 4,
                createdHandlers = 4,
                operationHookReleases = hookOwned ? 4 : 0,
                operationHandlerReleases = handlerOwned ? 4 : 0,
                finalHookReleases = mixed.ReleasedHooks.Count,
                finalHandlerReleases = probe.Released.Count,
                mixed.HookFactoryDisposals,
                mixed.HookDependencyDisposals,
                probe.FactoryDisposals,
                probe.DependencyDisposals
            });
        }
        finally { core.Cancellation.Dispose(); ReferenceSendProbe.Ambient.Value = ambient; CultureInfo.CurrentCulture = culture; }
    }
}
