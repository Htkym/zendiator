using System.Collections.Generic;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>D02/D03/D04/C01 requirements, derived from declarations without reading a candidate or its builder.</summary>
internal sealed class RequiredStreamExecutionContract
{
    internal readonly List<StreamExecutionSite> Sites = new();
    internal readonly List<StreamExecutionOwner> Owners = new();
    internal readonly List<StreamTokenRule> Tokens = new();
    internal readonly List<StreamExecutionAwait> Awaits = new();
    internal readonly List<StreamValueTransfer> Transfers = new();
    internal readonly List<StreamFailurePolicy> Failures = new();
    internal readonly List<StreamServiceResolution> ServiceResolutions = new();
    internal readonly List<StreamSessionCall> SessionCalls = new();
    internal readonly List<StreamCallbackReceiver> CallbackReceivers = new();

    internal static RequiredStreamExecutionContract Derive(BoundPipelineModel route) => Derive(new StreamSegmentContract(route.Anchor, StreamSourceKind.Handler, route.Handlers[0].Callback!, route.Stages)
        { SourceSessions = route.Handlers[0].Sessions });

    internal static RequiredStreamExecutionContract Derive(StreamSegmentContract contract)
    {
        var resolve = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.ResolveHandler : StreamPhase.ResolveWrapper;
        var factory = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory;
        var sourceName = contract.SourceKind == StreamSourceKind.Handler ? "handler" : "wrapper";
        var required = new RequiredStreamExecutionContract();
        required.Owners.Add(new("provider", null, StreamOwnerKind.Provider, StreamOwnerLifetime.Caller, true, StreamCleanup.None));
        required.Owners.Add(new("envelope", "provider", StreamOwnerKind.Envelope, StreamOwnerLifetime.Envelope, false, StreamCleanup.None));
        required.Owners.Add(new("cursor", "envelope", StreamOwnerKind.Cursor, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.None));
        required.Owners.Add(new("source", "cursor", StreamOwnerKind.SourceCursor, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.AsyncDispose));
        required.Owners.Add(new("linked-token", "cursor", StreamOwnerKind.LinkedToken, StreamOwnerLifetime.EachEnumeration, false, StreamCleanup.Dispose));
        for (var i = -1; i < contract.Hooks.Count; i++)
            required.Owners.Add(new(Service(i), "provider", StreamOwnerKind.Service, StreamOwnerLifetime.Caller, true, StreamCleanup.None));
        required.Tokens.Add(new(StreamTokenCase.ApiNotCancelable, StreamTokenSource.Enumerator, false));
        required.Tokens.Add(new(StreamTokenCase.EnumeratorNotCancelable, StreamTokenSource.Api, false));
        required.Tokens.Add(new(StreamTokenCase.SameToken, StreamTokenSource.Api, false));
        required.Tokens.Add(new(StreamTokenCase.DistinctCancelable, StreamTokenSource.Linked, true));

        Group(StreamProgram.Create, new[] { StreamPhase.NullGuard, StreamPhase.NullFailure, StreamPhase.CreateEnvelope, StreamPhase.EnvelopeReturn });
        Group(StreamProgram.GetEnumerator, new[] { StreamPhase.CreateCursor, StreamPhase.CursorReturn });
        Group(StreamProgram.Move, new[] { StreamPhase.MoveGuard, StreamPhase.ClearCurrent, StreamPhase.Stopped, StreamPhase.Started,
            StreamPhase.MergeTokens, StreamPhase.PreCancel, resolve, factory, StreamPhase.AcquireSource,
            StreamPhase.MarkStarted, StreamPhase.SourceGuard, StreamPhase.SourceMove, StreamPhase.SourceCurrent, StreamPhase.CacheCurrent,
            StreamPhase.StartupDetachLink, StreamPhase.StartupLinkPresent, StreamPhase.StartupDisposeLink, StreamPhase.EndFailure,
            StreamPhase.EndFalse, StreamPhase.ReleaseTrue, StreamPhase.ReleaseFalse, StreamPhase.ReleaseFailure,
            StreamPhase.MoveTrue, StreamPhase.MoveFalse, StreamPhase.MoveFailure, StreamPhase.BusyFailure });
        Group(StreamProgram.Current, new[] { StreamPhase.CurrentGuard, StreamPhase.ReadCache, StreamPhase.CurrentFailure });
        Group(StreamProgram.Dispose, new[] { StreamPhase.DisposeGuard, StreamPhase.AlreadyDisposed, StreamPhase.MarkDisposed,
            StreamPhase.DetachSource, StreamPhase.DetachLink, StreamPhase.SourcePresent, StreamPhase.DisposeSource,
            StreamPhase.LinkPresent, StreamPhase.DisposeLink, StreamPhase.ReleaseDispose, StreamPhase.DisposeResult,
            StreamPhase.DisposeSuccess, StreamPhase.DisposeFailure, StreamPhase.BusyFailure });
        for (var i = 0; i < contract.Hooks.Count; i++)
            Group(StreamProgram.Move, new[] { StreamPhase.HookGuard, StreamPhase.HookCached, StreamPhase.ResolveHook,
                contract.Hooks[i].Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map }, i);

        AddTransfer(StreamProgram.Create, StreamPhase.CreateEnvelope, "api/request", "envelope/request");
        AddTransfer(StreamProgram.Create, StreamPhase.CreateEnvelope, "api/token", "envelope/api-token");
        AddTransfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "envelope/request", "cursor/request");
        AddTransfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "envelope/api-token", "cursor/api-token");
        AddTransfer(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, "enumerator/token", "cursor/enumerator-token");
        AddTransfer(StreamProgram.Move, StreamPhase.MergeTokens, "token-selection/result", "cursor/start-token", StreamOutcome.Success);
        AddTransfer(StreamProgram.Move, factory, "cursor/request", sourceName + "/request", StreamOutcome.Success);
        AddTransfer(StreamProgram.Move, factory, "cursor/start-token", sourceName + "/token", StreamOutcome.Success);
        AddTransfer(StreamProgram.Move, StreamPhase.AcquireSource, sourceName + "/enumerable", "cursor/source", StreamOutcome.Success);
        // D02 applies to an explicit enumerator argument. C01 does not apply the outer Wrap
        // token again when the framework acquires Wrapper output, including direct Open returns.
        AddTransfer(StreamProgram.Move, StreamPhase.AcquireSource, contract.SourceKind == StreamSourceKind.Handler ? "cursor/start-token" : "CancellationToken.None", "source/token", StreamOutcome.Success);
        AddTransfer(StreamProgram.Move, StreamPhase.SourceCurrent, "source/Current", "cursor/item", StreamOutcome.Success);
        for (var i = 0; i < contract.Hooks.Count; i++)
        {
            var phase = contract.Hooks[i].Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map;
            AddTransfer(StreamProgram.Move, phase, "cursor/item", HookItem(i), StreamOutcome.Always, i);
            AddTransfer(StreamProgram.Move, phase, "cursor/request", "hook:" + i + "/request", StreamOutcome.Always, i);
            AddTransfer(StreamProgram.Move, phase, "cursor/start-token", "hook:" + i + "/token", StreamOutcome.Always, i);
            if (phase == StreamPhase.Map) AddTransfer(StreamProgram.Move, phase, "hook:" + i + "/result", "cursor/item", StreamOutcome.Success, i);
        }
        AddTransfer(StreamProgram.Move, StreamPhase.CacheCurrent, "cursor/item", "cursor/current-cache");
        AddTransfer(StreamProgram.Current, StreamPhase.ReadCache, "cursor/current-cache", "consumer/Current");
        AddTransfer(StreamProgram.Move, StreamPhase.StartupDetachLink, "cursor/linked-token", "startup/linked-token");
        AddTransfer(StreamProgram.Dispose, StreamPhase.DetachSource, "cursor/source", "dispose/source");
        AddTransfer(StreamProgram.Dispose, StreamPhase.DetachLink, "cursor/linked-token", "dispose/linked-token");
        required.Awaits.Add(new(Id(StreamProgram.Move, StreamPhase.SourceMove), "bool", true, true, "source-result", "ended-failure",
            new(new[] { "cursor/request", "cursor/start-token", "cursor/source", "cursor/hook-cache" })));
        required.Awaits.Add(new(Id(StreamProgram.Dispose, StreamPhase.DisposeSource), "void", true, true, "dispose-link", "replace-and-dispose-link",
            new(new[] { "dispose/source", "dispose/linked-token" })));
        RequiredStreamSessionContract.Append(contract, required);
        return required;

        void Group(StreamProgram program, IEnumerable<StreamPhase> phases, int stage = -1)
        {
            foreach (var phase in phases)
            {
                var id = Id(program, phase, stage);
                var owner = program == StreamProgram.Create ? "envelope" : phase is StreamPhase.SourceMove or StreamPhase.SourceCurrent or StreamPhase.DisposeSource ? "source" :
                    phase is StreamPhase.StartupDisposeLink or StreamPhase.DisposeLink ? "linked-token" : "cursor";
                string? producer = phase switch {
                    StreamPhase.SourceFactory or StreamPhase.WrapperFactory => contract.Callback.Implementation,
                    StreamPhase.Filter or StreamPhase.Map => contract.Hooks[stage].Callbacks[0].Implementation,
                    StreamPhase.SourceMove => "IAsyncEnumerator.MoveNextAsync",
                    StreamPhase.SourceCurrent => "IAsyncEnumerator.Current",
                    StreamPhase.DisposeSource => "IAsyncDisposable.DisposeAsync", _ => null
                };
                required.Sites.Add(new(id, stage < 0 ? contract.Anchor : contract.Hooks[stage].Anchor, owner,
                    phase is StreamPhase.SourceMove or StreamPhase.DisposeSource ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous, producer));
                if (phase is StreamPhase.MergeTokens or StreamPhase.PreCancel or StreamPhase.ResolveHandler or StreamPhase.ResolveWrapper or StreamPhase.SourceFactory or StreamPhase.WrapperFactory or
                    StreamPhase.AcquireSource or StreamPhase.StartupDisposeLink or StreamPhase.SourceGuard or StreamPhase.SourceMove or StreamPhase.SourceCurrent or
                    StreamPhase.HookGuard or StreamPhase.ResolveHook or StreamPhase.Filter or StreamPhase.Map or StreamPhase.DisposeSource or StreamPhase.DisposeLink)
                {
                    required.Failures.Add(new(id, true, true, true, false));
                    required.Transfers.Add(new("producer/error", "cursor/error", id, StreamOutcome.Failure));
                }
            }
        }
        void AddTransfer(StreamProgram program, StreamPhase phase, string source, string destination, StreamOutcome outcome = StreamOutcome.Always, int stage = -1) =>
            required.Transfers.Add(new(source, destination, Id(program, phase, stage), outcome));
    }
}
