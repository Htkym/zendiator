using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string SessionFactoryFixture(string family = "SendAsync", bool owned = true,
        bool asyncFactory = false, bool wrapper = false)
    {
        var sync = family is "SendSync" or "SendAllSync";
        var all = family is "SendAllSync" or "SendAllAsync";
        var role = wrapper ? "Wrapper" : "Hook";
        var request = family == "Stream" ? "Items" : "Ping";
        var source = family == "Stream" ? StreamGraphFixture("Stage." + role + "<OperationSession,Factory<string>>()") :
            FamilyFixture("c.Handlers<Ping>(DispatchFamily." + family + ",typeof(A)" + (all ? ",typeof(B)" : "") + ");" +
                "c.Pipeline<Ping>(Stage." + role + "<OperationSession,Factory<string>>());", sync, false);
        var contract = "I" + (asyncFactory ? "Async" : "") + (owned ? "Owned" : "Borrowed") + "SessionFactory<" + request + ",OperationSession>";
        var callbacks = family == "Stream"
            ? wrapper ? "IStreamWrapper<Items,int>" : "IStreamItemMap<Items,int>"
            : wrapper ? sync ? "ISendWrapper<Ping,int>" : "IAsyncSendWrapper<Ping,int>" : "ISendRequestTransform<Ping>";
        var body = family == "Stream"
            ? wrapper ? "public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c) where N:IStreamUpstream<Items,int> => n.Open(r,c);"
                : "public int Map(Items r,int item,CancellationToken c)=>item;"
            : wrapper ? sync ? "public int Invoke<N>(Ping r,N n,CancellationToken c) where N:ISendContinuation<Ping,int> => n.Invoke(r,c);"
                : "public ValueTask<int> InvokeAsync<N>(Ping r,N n,CancellationToken c) where N:IAsyncSendContinuation<Ping,int> => n.InvokeAsync(r,c);"
                : "public Ping Before(Ping r,CancellationToken c)=>r;";
        return source + "public sealed class OperationSession : " + callbacks + ",IDisposable,IAsyncDisposable {" + body +
            " public void Dispose(){} public ValueTask DisposeAsync()=>default; }" +
            "public class FactoryBase<T> : " + contract + " where T:class {" +
            (asyncFactory ? "ValueTask<OperationSession> " + contract + ".CreateAsync(" + request + " r,CancellationToken c)=>new(new OperationSession());"
                : "OperationSession " + contract + ".Create(" + request + " r,CancellationToken c)=>new();") +
            "} public sealed class Factory<T> : FactoryBase<T> where T:class {}";
    }

    [Theory]
    [InlineData("SendAsync", true, true, false)]
    [InlineData("SendAsync", false, false, true)]
    [InlineData("SendSync", true, false, true)]
    [InlineData("SendAllAsync", false, true, false)]
    [InlineData("SendAllSync", false, false, false)]
    [InlineData("Stream", true, false, false)]
    [InlineData("Stream", false, true, true)]
    public void Public_session_factory_binds_inherited_explicit_contract_and_ownership_but_blocks_receiver_emission(
        string family, bool owned, bool asyncFactory, bool wrapper)
    {
        var driver = BindLists(SessionFactoryFixture(family, owned, asyncFactory, wrapper), "ZEN0037");
        var stage = Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages);
        var session = Assert.Single(stage.Sessions);
        Assert.True(session.SuppliesCallbackReceiver);
        Assert.Equal(stage.Type, session.ResourceType);
        Assert.Contains("App.Factory`1", session.FactoryServiceType);
        Assert.Contains("System.String", session.FactoryServiceType);
        Assert.Equal(owned ? FusionOwnership.OwnedSession : FusionOwnership.BorrowedResource, stage.ServiceOwnership);
        Assert.Equal(stage.ServiceOwnership, session.Ownership);
        Assert.Equal(BoundCallbackPhase.SessionAcquire, session.Acquire.Phase);
        Assert.Equal(asyncFactory ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous, session.Acquire.Completion);
        Assert.Equal(asyncFactory, session.Acquire.RequiresSingleConsumption);
        Assert.Contains("explicit=True", session.Acquire.Implementation);
        Assert.Equal(2, session.Acquire.Parameters.Count);
        Assert.All(session.Acquire.Parameters, p => Assert.Equal("None", p.RefKind));
        if (owned)
        {
            Assert.Equal(BoundCallbackPhase.SessionCleanup, session.Cleanup!.Phase);
            var sync = family is "SendSync" or "SendAllSync";
            Assert.Equal(sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask, session.Cleanup.Completion);
            Assert.Equal(!sync, session.Cleanup.RequiresSingleConsumption);
        }
        else Assert.Null(session.Cleanup); // Disposable capability never grants ownership.
        var graph = Graph(driver);
        Assert.False(graph.IsValid);
        Assert.Contains(graph.Facts, f => f.Reason == (family == "Stream"
            ? "StreamSessionAcquisitionAndCleanupUnavailable" : "SessionCallbackReceiverGraphUnavailable"));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.Contains("Reads"));
    }

    [Theory]
    [InlineData("wrong-request", "ZEN0026")]
    [InlineData("wrong-session", "ZEN0026")]
    [InlineData("lookalike", "ZEN0025")]
    [InlineData("ambiguous-ownership", "ZEN0027")]
    [InlineData("ambiguous-completion", "ZEN0027")]
    [InlineData("async-on-sync", "ZEN0030")]
    [InlineData("missing-async-cleanup", "ZEN0025")]
    [InlineData("missing-sync-cleanup", "ZEN0025")]
    [InlineData("abstract-factory", "ZEN0035")]
    public void Public_session_factory_rejects_wrong_binding_without_inferring_body_or_disposal_ownership(string mutation, string diagnostic)
    {
        var source = SessionFactoryFixture(mutation is "async-on-sync" or "missing-sync-cleanup" ? "SendSync" : "SendAsync",
            asyncFactory: mutation == "async-on-sync");
        source = mutation switch
        {
            "wrong-request" => source.Replace("IOwnedSessionFactory<Ping,OperationSession>", "IOwnedSessionFactory<Other,OperationSession>")
                .Replace(".Create(Ping r", ".Create(Other r") + "public sealed class Other {}",
            "wrong-session" => source.Replace("IOwnedSessionFactory<Ping,OperationSession>", "IOwnedSessionFactory<Ping,Other>")
                .Replace("OperationSession IOwned", "Other IOwned") + "public sealed class Other {}",
            "lookalike" => source.Replace("IOwnedSessionFactory<Ping,OperationSession>", "Pretend.IOwnedSessionFactory<Ping,OperationSession>") +
                "public static class Pretend { public interface IOwnedSessionFactory<R,S> { S Create(R r,CancellationToken c); } }",
            "ambiguous-ownership" => source.Replace("public class FactoryBase<T> : IOwnedSessionFactory<Ping,OperationSession>",
                "public class FactoryBase<T> : IOwnedSessionFactory<Ping,OperationSession>,IBorrowedSessionFactory<Ping,OperationSession>")
                .Replace("} public sealed class Factory<T>", "OperationSession IBorrowedSessionFactory<Ping,OperationSession>.Create(Ping r,CancellationToken c)=>new(); } public sealed class Factory<T>"),
            "ambiguous-completion" => source.Replace("public class FactoryBase<T> : IOwnedSessionFactory<Ping,OperationSession>",
                "public class FactoryBase<T> : IOwnedSessionFactory<Ping,OperationSession>,IAsyncOwnedSessionFactory<Ping,OperationSession>")
                .Replace("} public sealed class Factory<T>", "ValueTask<OperationSession> IAsyncOwnedSessionFactory<Ping,OperationSession>.CreateAsync(Ping r,CancellationToken c)=>new(new OperationSession()); } public sealed class Factory<T>"),
            "missing-async-cleanup" => source.Replace(",IDisposable,IAsyncDisposable", ",IDisposable"),
            "missing-sync-cleanup" => source.Replace(",IDisposable,IAsyncDisposable", ",IAsyncDisposable"),
            "abstract-factory" => source.Replace("public sealed class Factory<T>", "public abstract class Factory<T>"),
            _ => source
        };
        var driver = BindLists(source, diagnostic);
        Assert.Empty(Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages).Sessions);
        var error = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Contains("Stage.", error.Location.SourceTree!.GetText().ToString(error.Location.SourceSpan));
    }

    [Fact]
    public void Factory_body_edits_keep_binding_cached_but_ownership_and_completion_edits_invalidate_it()
    {
        var source = SessionFactoryFixture();
        var driver = BindLists(source, "ZEN0037");
        var original = Selection(driver);
        driver = driver.RunGenerators(Compilation(source.Replace(".Create(Ping r,CancellationToken c)=>new();", ".Create(Ping r,CancellationToken c)=>throw new Exception();")));
        Assert.Equal(original, Selection(driver));
        SelectionReason(driver, IncrementalStepRunReason.Cached);
        foreach (var changed in new[] { SessionFactoryFixture(owned: false), SessionFactoryFixture(asyncFactory: true), source.Replace("where T:class", "where T:notnull") })
        {
            driver = driver.RunGenerators(Compilation(changed));
            Assert.NotEqual(original, Selection(driver));
            SelectionReason(driver, IncrementalStepRunReason.Modified);
        }
    }

    [Fact]
    public void Factory_session_cannot_use_a_DI_receiver_graph_even_when_service_ownership_is_forged_as_borrowed()
    {
        var driver = BindLists(SessionFactoryFixture(), "ZEN0037");
        var route = Assert.Single(Selection(driver).Pipelines);
        var forged = route with { Stages = new(route.Stages.Select(s => s with { ServiceOwnership = FusionOwnership.BorrowedService })) };
        var old = Graph(BindLists(SessionFactoryFixture().Replace("Stage.Hook<OperationSession,Factory<string>>()", "Stage.Hook<OperationSession>()"), "ZEN0037"));
        Assert.True(old.IsValid);
        Assert.Contains(CanonicalSendGraphValidator.Validate(forged, old.Schedule!), f => f.Reason == "SessionCallbackReceiverGraphUnavailable");
    }

    [Fact]
    public void Unbound_factory_type_parameter_does_not_fall_back_to_a_borrowed_DI_stage()
    {
        var source = SessionFactoryFixture().Replace("public static class Startup {", "public static class Startup<TFactory> {")
            .Replace("Stage.Hook<OperationSession,Factory<string>>()", "Stage.Hook<OperationSession,TFactory>()");
        var driver = BindLists(source, "ZEN0035");
        Assert.Empty(Assert.Single(Selection(driver).Pipelines).Stages);
        Assert.Contains("unbound type parameter", Assert.Single(driver.GetRunResult().Diagnostics).GetMessage());
    }

    [Fact]
    public void Missing_factory_implementation_remains_compiler_owned()
    {
        var source = SessionFactoryFixture().Replace("OperationSession IOwnedSessionFactory<Ping,OperationSession>.Create(Ping r,CancellationToken c)=>new();", "");
        var driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(source), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal("CS0535", Assert.Single(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).Id);
        Assert.True(Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages).CompilerOwnsError);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Owned_cleanup_binds_framework_implicit_and_explicit_Dispose_signatures(bool sync, bool explicitCleanup)
    {
        var source = SessionFactoryFixture(sync ? "SendSync" : "SendAsync");
        if (explicitCleanup) source = sync
            ? source.Replace("public void Dispose(){}", "void IDisposable.Dispose(){}")
            : source.Replace("public ValueTask DisposeAsync()=>default;", "ValueTask IAsyncDisposable.DisposeAsync()=>default;");
        var driver = BindLists(source, "ZEN0037");
        var session = Assert.Single(Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages).Sessions);
        var input = Compilation(source);
        var contract = sync ? input.GetSpecialType(SpecialType.System_IDisposable) :
            input.GetSpecialType(SpecialType.System_Object).ContainingAssembly.GetTypeByMetadataName("System.IAsyncDisposable")!;
        var method = Assert.Single(contract.GetMembers().OfType<IMethodSymbol>());
        Assert.Equal(Zendiator.SourceGenerator.MarkedHandlerDiscovery.Identity(contract), session.Cleanup!.Interface);
        Assert.Equal(Zendiator.SourceGenerator.MarkedHandlerDiscovery.Identity(method.ReturnType), session.Cleanup.ReturnType);
        Assert.Equal(sync ? "Dispose" : "DisposeAsync", method.Name);
        Assert.Empty(session.Cleanup.Parameters);
        Assert.Contains("/explicit=" + explicitCleanup, session.Cleanup.Implementation);
        Assert.Equal(!sync, session.Cleanup.RequiresSingleConsumption);
    }

    [Theory]
    [InlineData(false, "wrong-method")]
    [InlineData(false, "wrong-return")]
    [InlineData(false, "multiple-methods")]
    [InlineData(true, "wrong-method")]
    [InlineData(true, "wrong-return")]
    [InlineData(true, "multiple-methods")]
    public void Source_cleanup_homonyms_cannot_grant_Owned_cleanup_or_crash_generator(bool sync, string shape)
    {
        var name = sync ? "IDisposable" : "IAsyncDisposable";
        var method = sync ? "Dispose" : "DisposeAsync";
        var members = shape == "wrong-return" ? "int " + method + "();" :
            "int Other();" + (shape == "multiple-methods" ? "int Another();" : "");
        var source = SessionFactoryFixture(sync ? "SendSync" : "SendAsync");
        if (shape == "wrong-return") source = sync
            ? source.Replace("public void Dispose(){}", "public int Dispose()=>1;")
            : source.Replace("public ValueTask DisposeAsync()=>default;", "public int DisposeAsync()=>1;");
        else source = source.Replace("public void Dispose(){}", "public int Other()=>1; public int Another()=>2; public void Dispose(){}");
        var input = Compilation(source);
        input = input.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace System { public interface " + name + " {" + members + "} }",
            (CSharpParseOptions)input.SyntaxTrees.First().Options));
        Assert.NotEmpty(input.GetTypeByMetadataName("System." + name)!.DeclaringSyntaxReferences);
        var driver = Driver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        Assert.Null(Assert.Single(driver.GetRunResult().Results).Exception);
        Assert.Equal("ZEN0025", Assert.Single(diagnostics).Id);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZEN", StringComparison.Ordinal)));
        Assert.Empty(Assert.Single(Assert.Single(Selection(driver).Pipelines).Stages).Sessions);
    }
}
