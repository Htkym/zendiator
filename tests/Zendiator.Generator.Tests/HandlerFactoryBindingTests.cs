using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string HandlerFactoryFixture(string family = "SendAsync", bool owned = true, bool asyncFactory = false, bool isVoid = false)
    {
        var sync = family is "SendSync" or "SendAllSync";
        var all = family is "SendAllSync" or "SendAllAsync";
        var handler = family == "Stream" ? "ItemsHandler" : "A";
        var request = family == "Stream" ? "Items" : "Ping";
        var entry = "HandlerEntry.Session<" + handler + ",HandlerFactory<string>>()";
        var source = family == "Stream" ? StreamGraphFixture("").Replace("typeof(ItemsHandler)", entry) :
            FamilyFixture("c.Handlers<Ping>(DispatchFamily." + family + "," + entry + (all ? ",typeof(B)" : "") + ");", sync, isVoid);
        var contract = "I" + (asyncFactory ? "Async" : "") + (owned ? "Owned" : "Borrowed") + "SessionFactory<" + request + "," + handler + ">";
        source = source.Replace("public sealed class " + handler, "public sealed partial class " + handler);
        return source + "public sealed partial class " + handler + " : IDisposable,IAsyncDisposable { public void Dispose(){} public ValueTask DisposeAsync()=>default; }" +
            "public class HandlerFactoryBase<T> : " + contract + " where T:class {" +
            (asyncFactory ? "ValueTask<" + handler + "> " + contract + ".CreateAsync(" + request + " r,CancellationToken c)=>new(new " + handler + "());"
                : handler + " " + contract + ".Create(" + request + " r,CancellationToken c)=>new();") +
            "} public sealed class HandlerFactory<T> : HandlerFactoryBase<T> where T:class {}";
    }

    [Theory]
    [InlineData("SendSync", true, false, false)]
    [InlineData("SendSync", false, false, true)]
    [InlineData("SendAsync", true, true, false)]
    [InlineData("SendAsync", false, false, true)]
    [InlineData("SendAllSync", true, false, false)]
    [InlineData("SendAllSync", false, false, true)]
    [InlineData("SendAllAsync", true, false, false)]
    [InlineData("SendAllAsync", false, true, true)]
    [InlineData("Stream", true, false, false)]
    [InlineData("Stream", false, true, false)]
    public void Public_marked_Handler_factory_binds_explicit_contract_and_existing_Send_or_Stream_receiver_graph(
        string family, bool owned, bool asyncFactory, bool isVoid)
    {
        var source = HandlerFactoryFixture(family, owned, asyncFactory, isVoid);
        var driver = family is "SendSync" or "SendAsync" ? BindAcceptedSendLists(source) : BindLists(source,"ZEN0037");
        var route = Assert.Single(Selection(driver).Pipelines);
        var handler = route.Handlers[0]; var session = Assert.Single(handler.Sessions);
        Assert.Equal(handler.Type, session.ResourceType);
        Assert.True(session.SuppliesCallbackReceiver);
        Assert.Equal(owned ? FusionOwnership.OwnedSession : FusionOwnership.BorrowedResource, handler.ServiceOwnership);
        Assert.Equal(handler.ServiceOwnership, session.Ownership);
        Assert.Contains("App.HandlerFactory`1", session.FactoryServiceType);
        Assert.Contains("explicit=True", session.Acquire.Implementation);
        Assert.Equal(asyncFactory ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous, session.Acquire.Completion);
        Assert.Equal(owned, session.Cleanup != null);
        var graph = Graph(driver);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        if (family == "Stream")
        {
            var schedule = graph.StreamSchedule!;
            Assert.Equal(session.FactoryServiceType, Assert.Single(schedule.ServiceResolutions).ServiceType);
            Assert.Equal(StreamExecutionNames.Session(-1) + "/live", Assert.Single(schedule.CallbackReceivers).Receiver);
            var contract = new StreamSegmentContract(route.Anchor, StreamSourceKind.Handler, handler.Callback!, route.Stages) { SourceSessions = handler.Sessions };
            Assert.Empty(CanonicalStreamSessionValidator.Inspect(contract, schedule).Facts);
        }
        else
        {
            var schedule = graph.Schedule!;
            Assert.Equal(session.FactoryServiceType, Assert.Single(schedule.ServiceResolutions).ServiceType);
            Assert.Equal(SendExecutionNames.Session(0, 0, 0) + "/live", Assert.Single(schedule.CallbackReceivers).Receiver);
            Assert.Empty(CanonicalSendSessionValidator.Inspect(route, schedule).Facts);
        }
        if (family.StartsWith("SendAll", StringComparison.Ordinal))
        {
            Assert.Equal(new[] { "global::App.A", "global::App.B" }, route.Handlers.Select(h => h.Name));
            Assert.Empty(route.Handlers[1].Sessions);
        }
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.Contains("Reads"));
    }

    [Theory]
    [InlineData("missing-marker", "ZEN0035")]
    [InlineData("wrong-request", "ZEN0026")]
    [InlineData("wrong-session", "ZEN0026")]
    [InlineData("lookalike", "ZEN0025")]
    [InlineData("ambiguous-ownership", "ZEN0027")]
    [InlineData("async-on-sync", "ZEN0030")]
    [InlineData("missing-async-cleanup", "ZEN0025")]
    [InlineData("missing-sync-cleanup", "ZEN0025")]
    [InlineData("abstract-factory", "ZEN0035")]
    [InlineData("helper-entry", "ZEN0035")]
    [InlineData("lookalike-entry", "ZEN0035")]
    public void Public_Handler_factory_rejects_unmarked_mismatch_implicit_ownership_and_nonliteral_entry_without_DI_fallback(string mutation, string diagnostic)
    {
        var source = HandlerFactoryFixture(mutation is "async-on-sync" or "missing-sync-cleanup" ? "SendSync" : "SendAsync", asyncFactory: mutation == "async-on-sync");
        source = mutation switch {
            "missing-marker" => source.Replace("[ZendiatorHandler] public sealed partial class A", "public sealed partial class A")
                .Replace("public sealed class Handler :", "public abstract class Handler :"),
            "wrong-request" => source.Replace("IOwnedSessionFactory<Ping,A>", "IOwnedSessionFactory<Other,A>")
                .Replace(".Create(Ping r", ".Create(Other r") + "public sealed class Other {}",
            "wrong-session" => source.Replace("IOwnedSessionFactory<Ping,A>", "IOwnedSessionFactory<Ping,Other>")
                .Replace("A IOwnedSessionFactory", "Other IOwnedSessionFactory") + "public sealed class Other {}",
            "lookalike" => source.Replace("IOwnedSessionFactory<Ping,A>", "Pretend.IOwnedSessionFactory<Ping,A>") +
                "public static class Pretend { public interface IOwnedSessionFactory<R,S> { S Create(R r,CancellationToken c); } }",
            "ambiguous-ownership" => source.Replace("HandlerFactoryBase<T> : IOwnedSessionFactory<Ping,A>", "HandlerFactoryBase<T> : IOwnedSessionFactory<Ping,A>,IBorrowedSessionFactory<Ping,A>")
                .Replace("} public sealed class HandlerFactory<T>", "A IBorrowedSessionFactory<Ping,A>.Create(Ping r,CancellationToken c)=>new(); } public sealed class HandlerFactory<T>"),
            "missing-async-cleanup" => source.Replace("IDisposable,IAsyncDisposable", "IDisposable"),
            "missing-sync-cleanup" => source.Replace("IDisposable,IAsyncDisposable", "IAsyncDisposable"),
            "abstract-factory" => source.Replace("public sealed class HandlerFactory<T>", "public abstract class HandlerFactory<T>"),
            "helper-entry" => source.Replace("HandlerEntry.Session<A,HandlerFactory<string>>()", "Helpers.Make()") +
                "public static class Helpers { public static HandlerEntry Make()=>HandlerEntry.Session<A,HandlerFactory<string>>(); }",
            "lookalike-entry" => source.Replace("HandlerEntry.Session<A,HandlerFactory<string>>()", "PretendEntry.Session<A,HandlerFactory<string>>()") +
                "public static class PretendEntry { public static HandlerEntry Session<S,F>() where S:class => HandlerEntry.Session<S,F>(); }",
            _ => source
        };
        var driver = BindLists(source, diagnostic);
        Assert.All(Assert.Single(Selection(driver).Pipelines).Handlers, h => Assert.Empty(h.Sessions));
        var location = Assert.Single(driver.GetRunResult().Diagnostics).Location;
        var entryText = location.SourceTree!.GetText().ToString(location.SourceSpan);
        Assert.Contains(mutation == "helper-entry" ? "Helpers.Make()" : mutation == "lookalike-entry" ? "PretendEntry.Session" : "HandlerEntry.Session", entryText);
    }

    [Fact]
    public void Handler_factory_body_edits_cache_but_ownership_completion_and_constraints_invalidate_binding()
    {
        var source = HandlerFactoryFixture(); var driver = BindAcceptedSendLists(source); var original = Selection(driver);
        driver = driver.RunGenerators(Compilation(source.Replace(".Create(Ping r,CancellationToken c)=>new();", ".Create(Ping r,CancellationToken c)=>throw new Exception();")));
        Assert.Equal(original, Selection(driver)); SelectionReason(driver, IncrementalStepRunReason.Cached);
        foreach (var changed in new[] { HandlerFactoryFixture(owned: false), HandlerFactoryFixture(asyncFactory: true), source.Replace("where T:class", "where T:notnull") })
        {
            driver = driver.RunGenerators(Compilation(changed));
            Assert.NotEqual(original, Selection(driver)); SelectionReason(driver, IncrementalStepRunReason.Modified);
        }
    }

    [Fact]
    public void Handler_factory_type_parameter_cannot_fall_back_to_a_borrowed_DI_handler()
    {
        var source = HandlerFactoryFixture().Replace("public static class Startup {", "public static class Startup<TFactory> {")
            .Replace("HandlerEntry.Session<A,HandlerFactory<string>>()", "HandlerEntry.Session<A,TFactory>()");
        var driver = BindLists(source, "ZEN0035");
        Assert.Empty(Assert.Single(Selection(driver).Pipelines).Handlers);
        Assert.Contains("unbound type parameter", Assert.Single(driver.GetRunResult().Diagnostics).GetMessage());
    }

    [Fact]
    public void Literal_HandlerEntry_service_and_mixed_factory_list_keep_authoritative_order_and_duplicates()
    {
        var source = HandlerFactoryFixture("SendAllAsync").Replace(
            "HandlerEntry.Session<A,HandlerFactory<string>>(),typeof(B)", "typeof(B),HandlerEntry.Session<A,HandlerFactory<string>>()");
        var driver = BindLists(source, "ZEN0037");
        var route = Assert.Single(Selection(driver).Pipelines);
        Assert.Equal(new[] { "global::App.B", "global::App.A" }, route.Handlers.Select(h => h.Name));
        Assert.Empty(route.Handlers[0].Sessions); Assert.Single(route.Handlers[1].Sessions);
        var explicitService = BindLists(source.Replace("typeof(B)", "HandlerEntry.Service<B>()"), "ZEN0037");
        Assert.Equal(route.Handlers.Select(h => h.Type), Assert.Single(Selection(explicitService).Pipelines).Handlers.Select(h => h.Type));
        var duplicate = BindLists(source.Replace("typeof(B)", "HandlerEntry.Service(typeof(A))"), "ZEN0035");
        Assert.Contains("occurs more than once", Assert.Single(duplicate.GetRunResult().Diagnostics).GetMessage());
    }
}
