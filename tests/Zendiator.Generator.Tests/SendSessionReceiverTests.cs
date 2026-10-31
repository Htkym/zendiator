using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string SendReceiverFixture(string family, bool owned = true, bool asyncFactory = false,
        bool wrapper = false, bool isVoid = false, bool fullHook = false)
    {
        var source = SessionFactoryFixture(family, owned, asyncFactory, wrapper);
        var sync = family is "SendSync" or "SendAllSync";
        if (fullHook)
        {
            source = source.Replace("OperationSession : ISendRequestTransform<Ping>,",
                "OperationSession : ISendRequestTransform<Ping>,ISendResponseTransform<Ping,int>,ISendExceptionObserver<Ping>," +
                (sync ? "ISendFinallyObserver<Ping>," : "IAsyncSendFinallyObserver<Ping>,"));
            source = source.Replace("public Ping Before(Ping r,CancellationToken c)=>r;",
                "public Ping Before(Ping r,CancellationToken c)=>r; public int After(Ping r,int result,CancellationToken c)=>result;" +
                " public void OnException(Ping r,Exception e,CancellationToken c){} " +
                (sync ? "public void Finally(Ping r,SendExitInfo e,CancellationToken c){}" :
                    "public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;"));
        }
        if (isVoid)
            source = source.Replace("IRequest<int>", "IRequest").Replace("ISyncRequest<int>", "ISyncRequest")
            .Replace("IRequestHandler<Ping,int>", "IRequestHandler<Ping>").Replace("ISyncRequestHandler<Ping,int>", "ISyncRequestHandler<Ping>")
            .Replace("ValueTask<int> HandleAsync", "ValueTask HandleAsync").Replace("int Handle(", "void Handle(")
            .Replace("=> new(1);", "=> default;").Replace("=> 1;", "{}")
            .Replace("ISendWrapper<Ping,int>", "ISendWrapper<Ping>").Replace("IAsyncSendWrapper<Ping,int>", "IAsyncSendWrapper<Ping>")
            .Replace("ISendContinuation<Ping,int>", "ISendContinuation<Ping>").Replace("IAsyncSendContinuation<Ping,int>", "IAsyncSendContinuation<Ping>")
            .Replace("int Invoke<", "void Invoke<").Replace("ValueTask<int> InvokeAsync", "ValueTask InvokeAsync")
            .Replace("ISendResponseTransform<Ping,int>", "ISendSuccessObserver<Ping>")
            .Replace("public int After(Ping r,int result,CancellationToken c)=>result;", "public void After(Ping r,CancellationToken c){}");
        return source;
    }

    [Theory]
    [InlineData("SendSync", true, false, false, false)]
    [InlineData("SendSync", false, false, true, true)]
    [InlineData("SendAsync", true, true, false, false)]
    [InlineData("SendAsync", false, false, true, true)]
    [InlineData("SendAllSync", true, false, true, false)]
    [InlineData("SendAllSync", false, false, false, true)]
    [InlineData("SendAllAsync", true, false, false, false)]
    [InlineData("SendAllAsync", false, true, true, true)]
    public void Public_factory_Send_graph_resolves_factory_once_and_calls_returned_session(
        string family, bool owned, bool asyncFactory, bool wrapper, bool isVoid)
    {
        var source = SendReceiverFixture(family, owned, asyncFactory, wrapper, isVoid);
        var acceptsRuntime = family is "SendSync" or "SendAsync" || (wrapper && (family is "SendAllSync" or "SendAllAsync"));
        var driver = acceptsRuntime ? BindAcceptedSendLists(source) : BindLists(source, "ZEN0037");
        var graph = Graph(driver);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = graph.Schedule!;
        var session = Assert.Single(graph.Contract.Stages[0].Sessions);
        Assert.Equal(isVoid ? FusionResultKind.Void : FusionResultKind.Response, graph.Contract.ResultKind);
        Assert.Empty(CanonicalSendSessionValidator.Validate(graph.Contract, schedule));
        for (var branch = 0; branch < graph.Contract.Handlers.Count; branch++)
        {
            var resolution = Assert.Single(schedule.ServiceResolutions, r => r.At.Branch == branch);
            Assert.Equal(Site(branch, 0, SendExecutionPhase.Resolve), resolution.At);
            Assert.Equal(session.FactoryServiceType, resolution.ServiceType);
            Assert.Equal(Service(branch, 0) + "/resolved", resolution.ResultSlot);
            Assert.DoesNotContain(schedule.Sites, s => s.Id.Branch == branch && s.Id.Phase == SendExecutionPhase.ResolveSessionFactory);
            var acquire = Assert.Single(schedule.SessionCalls, c => c.At == SessionSite(branch, 0, 0, SendExecutionPhase.AcquireSession));
            Assert.Equal(resolution.ResultSlot, acquire.Receiver);
            var state = Assert.Single(schedule.States, s => s.Owner == Scope(branch, 0));
            Assert.Equal(state.InputRequestSlot, acquire.RequestSlot);
            Assert.Equal(state.StartTokenSlot, acquire.TokenSlot);
            var body = Assert.Single(schedule.CallbackReceivers, r => r.At.Branch == branch);
            Assert.Equal(Session(branch, 0, 0) + "/live", body.Receiver);
            Assert.NotEqual(acquire.Receiver, body.Receiver);
            Assert.Equal(wrapper ? SendExecutionPhase.InvokeWrapper : SendExecutionPhase.Before, body.At.Phase);
            var owner = Assert.Single(schedule.Owners, o => o.Id == Session(branch, 0, 0));
            Assert.Equal(!owned, owner.Borrowed);
            Assert.Equal(owned, schedule.SessionCalls.Any(c => c.At == SessionSite(branch, 0, 0, SendExecutionPhase.CleanupSession)));
        }
        Assert.All(schedule.Owners.Where(o => o.Kind == SendOwnerKind.Service), o =>
        {
            Assert.True(o.Borrowed);
            Assert.Equal(FusionCleanupKind.None, o.Cleanup);
        });
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.Contains("Reads"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Returned_session_supplies_all_Hook_phases_and_null_or_startup_failure_never_enters_or_disposes(bool sync)
    {
        var graph = Graph(BindAcceptedSendLists(SendReceiverFixture(sync ? "SendSync" : "SendAsync", asyncFactory: !sync, fullHook: true)));
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = graph.Schedule!;
        Assert.Equal(new[] { SendExecutionPhase.Before, SendExecutionPhase.Success, SendExecutionPhase.Exception, SendExecutionPhase.Finally },
            schedule.CallbackReceivers.Select(c => c.At.Phase));
        Assert.All(schedule.CallbackReceivers, c => Assert.Equal(Session(0, 0, 0) + "/live", c.Receiver));
        var inspection = CanonicalSendSessionValidator.Inspect(graph.Contract, schedule);
        Assert.Empty(inspection.Facts);
        var paths = inspection.Paths.Where(p => p.Owner == Scope(0, 0)).ToArray();
        foreach (var phase in new[] { SendExecutionPhase.SessionGuard, SendExecutionPhase.AcquireSession, SendExecutionPhase.ValidateSessionResult })
        {
            var failed = SessionSite(0, 0, 0, phase);
            Assert.Contains(paths, p => p.FailureOrder.Contains("fault:" + failed) && p.Acquired.Count == 0 && p.CleanupOrder.Count == 0 &&
                !p.Visited.Contains(Site(0, 0, SendExecutionPhase.Entered)) && !p.Visited.Contains(Site(0, 0, SendExecutionPhase.Finally)));
        }
        Assert.Contains(paths, p => p.EscapingError == null && p.Acquired.SequenceEqual(new[] { 0 }) && p.CleanupOrder.SequenceEqual(new[] { 0 }));
        Assert.Contains(paths, p => p.FailureOrder.Contains("cancel:" + Site(0, 0, SendExecutionPhase.InvokeInner)) &&
            p.EscapingError == "fault:" + SessionSite(0, 0, 0, SendExecutionPhase.CleanupSession) && !p.Canceled);
        Assert.Contains(paths, p => p.FailureOrder.Contains("fault:" + Site(0, 0, SendExecutionPhase.Finally)) && p.CleanupOrder.SequenceEqual(new[] { 0 }));
    }

    [Theory]
    [InlineData("factory-receiver", "BodyReceiverIsNotLiveTransferredSession")]
    [InlineData("factory-result-receiver", "BodyReceiverIsNotLiveTransferredSession")]
    [InlineData("other-session-receiver", "BodyReceiverIsNotLiveTransferredSession")]
    [InlineData("callback-request", "BodySessionRequestOrTokenSlotMismatch")]
    [InlineData("callback-token", "BodySessionRequestOrTokenSlotMismatch")]
    [InlineData("DI-session-type", "SessionFactoryDIResolutionTypeOrSlotMismatch")]
    [InlineData("DI-result-slot", "SessionFactoryDIResolutionTypeOrSlotMismatch")]
    [InlineData("acquire-session-receiver", "FactoryCallReceiverOrResultMismatch")]
    [InlineData("acquire-request", "SessionFactoryRequestOrTokenSlotMismatch")]
    [InlineData("acquire-token", "SessionFactoryRequestOrTokenSlotMismatch")]
    [InlineData("acquire-result-slot", "FactoryCallReceiverOrResultMismatch")]
    [InlineData("retain-result-alias", "FactoryResultAliasRetainedAfterTransfer")]
    [InlineData("pending-factory-lost", "SessionFactoryLostAcrossPendingAwait")]
    [InlineData("pending-body-lease-lost", "SessionLeaseLostAcrossPendingAwait")]
    public void Actual_factory_receiver_operands_reject_wrong_values_without_Required_or_builder_oracle(string mutation, string reason)
    {
        var graph = Graph(BindAcceptedSendLists(SendReceiverFixture("SendAsync", asyncFactory: true, fullHook: true)));
        var original = graph.Schedule!;
        var resource = Session(0, 0, 0);
        var candidate = mutation switch
        {
            "factory-receiver" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { Receiver = Service(0, 0) + "/resolved" })) },
            "factory-result-receiver" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { Receiver = resource + "/factory-result" })) },
            "other-session-receiver" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { Receiver = Session(0, 1, 0) + "/live" })) },
            "callback-request" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { RequestSlot = "other/request" })) },
            "callback-token" => original with { CallbackReceivers = new(original.CallbackReceivers.Select(c => c with { TokenSlot = "other/token" })) },
            "DI-session-type" => original with { ServiceResolutions = new(original.ServiceResolutions.Select(c => c with { ServiceType = graph.Contract.Stages[0].Type })) },
            "DI-result-slot" => original with { ServiceResolutions = new(original.ServiceResolutions.Select(c => c with { ResultSlot = resource + "/live" })) },
            "acquire-session-receiver" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == SendExecutionPhase.AcquireSession ? c with { Receiver = resource + "/live" } : c)) },
            "acquire-request" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == SendExecutionPhase.AcquireSession ? c with { RequestSlot = "other/request" } : c)) },
            "acquire-token" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == SendExecutionPhase.AcquireSession ? c with { TokenSlot = "other/token" } : c)) },
            "acquire-result-slot" => original with { SessionCalls = new(original.SessionCalls.Select(c => c.At.Phase == SendExecutionPhase.AcquireSession ? c with { Result = "other/result" } : c)) },
            "retain-result-alias" => original with { SessionTransfers = new(original.SessionTransfers.Where(t => !(t.At.Phase == SendExecutionPhase.CommitSession && t.Source == "null"))) },
            "pending-factory-lost" => original with { Awaits = new(original.Awaits.Select(a => a with { RetainedSlots = new(a.RetainedSlots.Where(s => s != Service(0, 0) + "/resolved")) })) },
            "pending-body-lease-lost" => original with
            {
                Awaits = new(original.Awaits.Select(a => a.Producer.Phase == SendExecutionPhase.Finally
                ? a with
                {
                    RetainedSlots = new(a.RetainedSlots.Where(s => s != resource + "/live"))
                } : a))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.Equal(original.Sites, candidate.Sites);
        Assert.Equal(original.ControlFlows, candidate.ControlFlows);
        Assert.Contains(CanonicalSendSessionValidator.Inspect(graph.Contract, candidate).Facts, f => f.Reason == reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_preserving_CFG_cannot_skip_nonnull_validation_or_commit_after_its_failure(bool afterFailure)
    {
        var graph = Graph(BindAcceptedSendLists(SendReceiverFixture("SendAsync", asyncFactory: true)));
        var original = graph.Schedule!;
        var candidate = ChangeControl(original, Scope(0, 0), e => e.From.Site?.Phase ==
            (afterFailure ? SendExecutionPhase.ValidateSessionResult : SendExecutionPhase.AcquireSession) &&
            e.Outcome == (afterFailure ? SendControlOutcome.Failure : SendControlOutcome.Success)
                ? e with
                {
                    To = Point(Scope(0, 0), SessionSite(0, 0, 0, SendExecutionPhase.CommitSession))
                } : e);
        Assert.Equal(original, candidate with
        {
            ControlFlows = original.ControlFlows
        });
        Assert.Contains(CanonicalSendSessionValidator.Validate(graph.Contract, candidate), f => f.Reason == "OwnershipTransferredBeforeNonNullSessionValidation");
        Assert.Contains(CanonicalSendControlValidator.Validate(graph.Contract, candidate), f => f.Reason == "OwnershipTransferredBeforeNonNullSessionValidation");
    }

    [Fact]
    public void Borrowed_factory_session_rejects_reachable_cleanup_even_when_it_implements_disposal()
    {
        var graph = Graph(BindAcceptedSendLists(SendReceiverFixture("SendAsync", owned: false)));
        var original = graph.Schedule!;
        var owner = Scope(0, 0);
        var cleanup = SessionSite(0, 0, 0, SendExecutionPhase.CleanupSession);
        var result = Point(owner, SessionSite(0, 0, -1, SendExecutionPhase.SessionResult));
        var candidate = ChangeControl(original, owner, e => e.From.Site?.Phase == SendExecutionPhase.DetachSession ? e with { To = Point(owner, cleanup) } : e);
        candidate = candidate with
        {
            SessionCalls = new(candidate.SessionCalls.Append(new(cleanup, Session(0, 0, 0) + "/detached", null, true, true, false))),
            ControlFlows = new(candidate.ControlFlows.Select(f => f.Owner != owner ? f : f with
            {
                Edges = new(f.Edges.Concat(new[] {
                new SendControlEdge(Point(owner, cleanup), result, SendControlOutcome.Success, SendControlPredicate.Any),
                new SendControlEdge(Point(owner, cleanup), result, SendControlOutcome.Failure, SendControlPredicate.Any) }))
            }))
        };
        Assert.Contains(CanonicalSendSessionValidator.Validate(graph.Contract, candidate), f => f.Reason == "BorrowedResourceDisposedByOperation");
    }

    [Fact]
    public void Public_nested_factory_wrapper_and_Hook_keep_each_next_state_and_partial_startup_obligations()
    {
        var source = SendReceiverFixture("SendAsync", asyncFactory: true, wrapper: true)
            .Replace("Stage.Wrapper<OperationSession,Factory<string>>()", "Stage.Wrapper<OperationSession,Factory<string>>(),Stage.Hook<InnerSession,InnerFactory>()") + """
            public sealed class InnerSession : ISendRequestTransform<Ping>,IAsyncSendFinallyObserver<Ping>,IAsyncDisposable {
                public Ping Before(Ping r,CancellationToken c)=>r;
                public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken c)=>default;
                public ValueTask DisposeAsync()=>default;
            }
            public sealed class InnerFactory : IAsyncOwnedSessionFactory<Ping,InnerSession> {
                public ValueTask<InnerSession> CreateAsync(Ping r,CancellationToken c)=>new(new InnerSession());
            }
            """;
        var graph = Graph(BindAcceptedSendLists(source));
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = graph.Schedule!;
        Assert.Equal(SendOwnerMultiplicity.EachNext, Assert.Single(schedule.Owners, o => o.Id == Session(0, 1, 0)).Multiplicity);
        Assert.Equal(Session(0, 1, 0) + "/live", Assert.Single(schedule.CallbackReceivers, c => c.At == Site(0, 1, SendExecutionPhase.Before)).Receiver);
        var inspection = CanonicalSendSessionValidator.Inspect(graph.Contract, schedule);
        Assert.Empty(inspection.Facts);
        Assert.Contains(inspection.Paths, p => p.Owner == Scope(0, 1) && p.Acquired.Count == 0 && p.CleanupOrder.Count == 0 &&
            p.EscapingError == "fault:" + SessionSite(0, 1, 0, SendExecutionPhase.AcquireSession) && !p.Visited.Contains(Site(0, 1, SendExecutionPhase.Finally)));
        // Child failures are summarized at the opaque wrapper call. Its user body decides next count/recovery.
        Assert.Contains(inspection.Paths, p => p.Owner == Scope(0, 0) && p.Acquired.SequenceEqual(new[] { 0 }) &&
            p.CleanupOrder.SequenceEqual(new[] { 0 }) && p.EscapingError == "fault:" + Site(0, 0, SendExecutionPhase.InvokeWrapper));
    }
}
