using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false, "default")]
    [InlineData(true, "default")]
    [InlineData(false, "static")]
    [InlineData(true, "static")]
    [InlineData(false, "default-static")]
    [InlineData(true, "default-static")]
    [InlineData(false, "inferred")]
    [InlineData(true, "inferred")]
    public void Enabled_single_Hook_public_registration_forms_compile_and_match_reference(bool isVoid, string form)
    {
        var named = form is not ("default" or "default-static");
        var extension = form is not ("static" or "default-static");
        var source = SingleHookBackendSource(isVoid);
        if (!named)
            source = source.Replace("services.AddZendiator<Reads>", "services.AddZendiator")
            .Replace("App.Generated.Reads", "Zendiator.Generated.Default");
        if (!extension)
            source = source.Replace("services.AddZendiator" + (named ? "<Reads>" : "") + "(c=>",
            "global::Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions.AddZendiator" + (named ? "<Reads>" : "") + "(services,c=>");
        if (form == "inferred")
            source = source.Replace("c.Pipeline<Ping>(DispatchFamily.SendSync,", "c.Pipeline<Ping>(");
        var f = CompileSyncFixture("direct", isVoid, named, extension, sourceOverride: source);
        const string golden = "hook.ctor before handler.ctor handler after finally";
        Assert.Equal(RunSingleHookEngine(f, isVoid, "success", golden, false), RunSingleHookEngine(f, isVoid, "success", golden, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_async_single_Hook_public_API_emits_SendAsync_backend(bool isVoid)
    {
        var source = SingleHookBackendSource(isVoid, "async").Replace("ISyncRequest", "IRequest").Replace("DispatchFamily.SendSync", "DispatchFamily.SendAsync");
        source = isVoid ? source.Replace("void IRequestHandler<Ping>.Handle(Ping request,CancellationToken token) { _probe.Handle(request,token); }",
            "global::System.Threading.Tasks.ValueTask IRequestHandler<Ping>.HandleAsync(Ping request,CancellationToken token) { _probe.Handle(request,token); return default; }") :
            source.Replace("int IRequestHandler<Ping,int>.Handle(Ping request,CancellationToken token) { return _probe.Handle(request,token); }",
                "global::System.Threading.Tasks.ValueTask<int> IRequestHandler<Ping,int>.HandleAsync(Ping request,CancellationToken token)=>global::System.Threading.Tasks.ValueTask.FromResult(_probe.Handle(request,token));");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "UnacceptedAsyncHook" + isVoid), out var output, out var diagnostics);
        AssertAcceptedSendEmission(driver, diagnostics, global::Zendiator.SourceGenerator.BoundDispatchFamily.SendAsync, isVoid);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
    }
}
