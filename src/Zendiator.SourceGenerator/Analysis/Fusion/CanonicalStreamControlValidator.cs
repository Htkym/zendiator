using System;
using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>
/// Executes candidate control paths over finite symbolic lifecycle states. Obligations come from the
/// bound declaration, not from candidate metadata. Filter retries preserve the cursor but reset item guards.
/// </summary>
internal static class CanonicalStreamControlValidator
{
    private sealed record Lifecycle(bool Started = false, bool Ended = false, bool Disposed = false,
        bool Source = false, bool Link = false, bool Cache = false, string Resolved = "", string LiveSessions = "");
    private sealed record Path(Lifecycle Life, bool Busy = false, bool Prepared = false, bool StoppedChecked = false,
        bool StartChecked = false, bool TokenMerged = false, bool PreCancel = false, bool Handler = false,
        bool Enumerable = false, bool SourceGuard = false, bool SourceCompleted = false, bool SourceSuccess = false, bool CurrentRead = false,
        int NextStage = 0, int GuardedStage = -1, bool Cached = false, bool Failure = false,
        bool StartupDetached = false, bool SourceDetached = false, bool LinkDetached = false,
        bool DetachedSource = false, bool DetachedLink = false, bool SourceCleaned = false, bool LinkCleaned = false,
        bool DisposeBegan = false, bool EndMarked = false, bool EnvelopeCreated = false, bool CursorCreated = false,
        bool NullChecked = false, bool CurrentChecked = false, bool DisposedChecked = false, bool MisuseFailure = false,
        string GuardedSessions = "", string Factories = "", string ReturnedSessions = "", string ValidatedSessions = "",
        string DetachedSessions = "", string CleanedSessions = "");
    private sealed record Visit(StreamSiteId Site, Path State);
    private sealed record Work(Visit Visit, EquatableArray<Visit> Trail);

    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(BoundPipelineModel route, CanonicalStreamExecutionSchedule candidate) =>
        Validate(new StreamSegmentContract(route.Anchor, StreamSourceKind.Handler, route.Handlers[0].Callback!, route.Stages)
        { SourceSessions = route.Handlers[0].Sessions }, candidate);

    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(StreamSegmentContract contract, CanonicalStreamExecutionSchedule candidate)
    {
        var facts = new HashSet<CanonicalExecutionGraphFact>();
        void Reject(string reason) => facts.Add(new(reason, contract.Anchor));
        var sessions = Enumerable.Range(-1, contract.Hooks.Count + 1).Where(i => Sessions(contract, i).Count != 0).ToArray();
        bool Has(string value, int position) => value.Contains("|" + position + "|");
        bool Cleaned(Path state) => sessions.All(i => Sessions(contract, i)[0].Ownership != FusionOwnership.OwnedSession ||
            !Has(state.DetachedSessions, i) || Has(state.CleanedSessions, i));
        var required = new HashSet<StreamSiteId>(RequiredStreamExecutionContract.Derive(contract).Sites.Select(s => s.Id));
        var programs = candidate.Flows.GroupBy(f => f.Program).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (StreamProgram program in Enum.GetValues(typeof(StreamProgram)))
            if (!programs.TryGetValue(program, out var group) || group.Length != 1) Reject("MissingOrDuplicateStreamProgram");
        if (programs.Keys.Any(p => !Enum.IsDefined(typeof(StreamProgram), p))) Reject("UnknownStreamProgram");
        foreach (var flow in candidate.Flows)
        {
            if (!required.Contains(flow.Entry) || flow.Entry != Entry(flow.Program)) Reject("StreamEntryMismatch");
            foreach (var edge in flow.Edges)
                if (edge.From.Program != flow.Program || edge.To.Program != flow.Program || !required.Contains(edge.From) || !required.Contains(edge.To))
                    Reject("UnknownOrCrossProgramControlPoint");
            foreach (var group in flow.Edges.GroupBy(e => e.From))
                if (group.Select(e => e.Outcome).Distinct().Count() != group.Count()) Reject("AmbiguousStreamOutcome");
        }
        if (facts.Count != 0) return new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal));
        var flows = programs.ToDictionary(g => g.Key, g => g.Value[0]);
        var reached = new HashSet<StreamSiteId>();
        var pathRejected = false;
        Run(flows[StreamProgram.Create], new(new()), false);
        Run(flows[StreamProgram.GetEnumerator], new(new()), false);
        // Cross-call closure covers unstarted/started/ended/disposed, replay, early break,
        // partial Hook caches, next Move and sequential Dispose after successful or failed cleanup.
        foreach (var linked in new[] { false, true })
        {
            var pending = new Queue<Lifecycle>();
            var seen = new HashSet<Lifecycle>();
            pending.Enqueue(new());
            while (pending.Count != 0)
            {
                var life = pending.Dequeue();
                if (!seen.Add(life)) continue;
                foreach (var program in new[] { StreamProgram.Move, StreamProgram.Current, StreamProgram.Dispose })
                {
                    foreach (var end in Run(flows[program], new(life), linked))
                        if (end != life) pending.Enqueue(end);
                    Run(flows[program], new(life, Busy: true), linked);
                }
            }
        }
        foreach (var missing in required.Where(s => !reached.Contains(s))) Reject("UnreachableStreamSite:" + missing.Phase);
        return new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal));

        IEnumerable<Lifecycle> Run(StreamControlFlow flow, Path initial, bool linked)
        {
            var endpoints = new HashSet<Lifecycle>();
            var todo = new Queue<Work>();
            var visited = new HashSet<Visit>();
            todo.Enqueue(new(new(flow.Entry, initial), default));
            var adjacency = flow.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToArray());
            while (todo.Count != 0)
            {
                var work = todo.Dequeue();
                var visit = work.Visit;
                if (work.Trail.Contains(visit))
                {
                    Reject("NonProgressStreamControlCycle");
                    continue;
                }
                if (!visited.Add(visit)) continue;
                var id = visit.Site;
                var state = visit.State;
                reached.Add(id);
                if (!required.Contains(id)) { Reject("UnexpectedStreamControlSite"); continue; }
                var outcomes = Outcomes(id, state);
                if (outcomes.Length == 0)
                {
                    CheckExit(id, state);
                    if (adjacency.ContainsKey(id)) Reject("ExecutionAfterStreamReturn");
                    endpoints.Add(state.Life);
                    continue;
                }
                foreach (var outcome in outcomes)
                {
                    var selected = adjacency.TryGetValue(id, out var outgoing) ? outgoing.Where(e => e.Outcome == outcome).ToArray() : Array.Empty<StreamControlEdge>();
                    if (selected.Length != 1) { Reject("MissingStreamOutcome:" + id.Phase + ":" + outcome); continue; }
                    pathRejected = false;
                    var next = Apply(id, state, outcome, linked);
                    // A rejected item starts a fresh guarded source attempt. Forget only that
                    // completed item path; global state dedup still terminates arbitrary retries.
                    var retry = id.Phase == StreamPhase.Filter && outcome == StreamOutcome.False && selected[0].To == Id(StreamProgram.Move, StreamPhase.SourceGuard);
                    if (!pathRejected) todo.Enqueue(new(new(selected[0].To, next), retry ? default : new(work.Trail.Append(visit))));
                }
                if (adjacency.TryGetValue(id, out var actual) && actual.Any(e => !AllOutcomes(id.Phase).Contains(e.Outcome))) Reject("UnexpectedStreamOutcome:" + id.Phase);
            }
            if (endpoints.Count == 0) Reject("StreamProgramHasNoExit");
            return endpoints;
        }

        StreamOutcome[] Outcomes(StreamSiteId id, Path s)
        {
            bool? decision = id.Phase switch
            {
                StreamPhase.Stopped => s.Life.Ended || s.Life.Disposed,
                StreamPhase.Started => s.Life.Started,
                StreamPhase.HookCached => HasService(s.Life, id.Stage),
                StreamPhase.StartupLinkPresent or StreamPhase.LinkPresent => s.DetachedLink,
                StreamPhase.SourcePresent => s.DetachedSource,
                StreamPhase.SessionPresent => Has(s.DetachedSessions, id.Stage),
                StreamPhase.CurrentGuard => s.Life.Cache && !s.Busy && !s.Life.Ended && !s.Life.Disposed,
                StreamPhase.AlreadyDisposed => s.Life.Disposed,
                StreamPhase.DisposeResult => s.Failure,
                _ => null
            };
            if (decision != null) return new[] { decision.Value ? StreamOutcome.True : StreamOutcome.False };
            if (id.Phase is StreamPhase.MoveGuard or StreamPhase.DisposeGuard) return new[] { s.Busy ? StreamOutcome.Failure : StreamOutcome.Success };
            // An absent linked resource cannot throw while being disposed.
            if (id.Phase is StreamPhase.StartupDisposeLink or StreamPhase.DisposeLink && !s.DetachedLink) return new[] { StreamOutcome.Success };
            return AllOutcomes(id.Phase);
        }
        Path Apply(StreamSiteId id, Path s, StreamOutcome outcome, bool linked)
        {
            var life = s.Life;
            var failed = outcome == StreamOutcome.Failure;
            bool Need(bool value, string reason) { if (!value) { Reject(reason); pathRejected = true; } return value; }
            if (s.MisuseFailure) { Reject("ExecutionAfterBusyRejection"); pathRejected = true; return s; }
            if (id.Program is StreamProgram.Create or StreamProgram.GetEnumerator && id.Phase is not
                (StreamPhase.NullGuard or StreamPhase.CreateEnvelope or StreamPhase.CreateCursor)) Reject("EagerStreamExecution");
            switch (id.Phase)
            {
                case StreamPhase.NullGuard: return s with { NullChecked = !failed, Failure = failed };
                case StreamPhase.CreateEnvelope:
                    Need(s.NullChecked && !s.EnvelopeCreated, "EnvelopeBeforeNullGuardOrRepeated");
                    return s with { EnvelopeCreated = true };
                case StreamPhase.CreateCursor:
                    Need(!s.CursorCreated, "CursorCreationRepeated"); return s with { CursorCreated = true };
                case StreamPhase.MoveGuard:
                    Need(!s.Busy || failed, "PendingMoveWasAccepted"); return failed ? s with { MisuseFailure = true } : s with { Busy = true };
                case StreamPhase.ClearCurrent:
                    Need(s.Busy && !s.Prepared, "MovePreparationMismatch"); return s with { Prepared = true, Life = life with { Cache = false } };
                case StreamPhase.Stopped: Need(s.Prepared, "StoppedCheckBeforeMovePreparation"); return s with { StoppedChecked = true };
                case StreamPhase.Started: Need(s.StoppedChecked && !life.Ended && !life.Disposed && !s.SourceCompleted, "RestartedStoppedCursor"); return s with { StartChecked = true };
                case StreamPhase.MergeTokens:
                    Need(s.StartChecked && !life.Started && !s.TokenMerged, "TokenMergeOutsideFirstMove");
                    return s with { TokenMerged = !failed, Failure = failed, Life = life with { Link = !failed && linked } };
                case StreamPhase.PreCancel:
                    Need(s.TokenMerged, "PreCancelBeforeTokenMerge"); return s with { PreCancel = !failed, Failure = failed };
                case StreamPhase.ResolveHandler:
                case StreamPhase.ResolveWrapper:
                    Need(s.PreCancel && !s.Handler && !s.Failure, "HandlerResolveWithoutPreCancel"); return s with { Handler = !failed, Failure = failed };
                case StreamPhase.SessionGuard:
                    Need(s.PreCancel && !life.Started && !life.Ended && !life.Disposed && !s.Failure && !Has(s.GuardedSessions, id.Stage), "SessionAcquisitionOutsideLazyStartup");
                    return s with { GuardedSessions = failed ? s.GuardedSessions : s.GuardedSessions + "|" + id.Stage + "|", Failure = failed };
                case StreamPhase.ResolveSessionFactory:
                    Need(Has(s.GuardedSessions, id.Stage) && !Has(s.Factories, id.Stage) && !s.Failure, "SessionFactoryResolveWithoutGuardOrRepeated");
                    return s with { Factories = failed ? s.Factories : s.Factories + "|" + id.Stage + "|", Failure = failed };
                case StreamPhase.AcquireSession:
                    Need(Has(s.GuardedSessions, id.Stage) && (id.Stage < 0 ? s.Handler : Has(s.Factories, id.Stage)) &&
                        !life.Started && !Has(life.LiveSessions, id.Stage) && !s.Failure, "SessionFactoryCallBeforeResolveOrRepeated");
                    return s with { ReturnedSessions = failed ? s.ReturnedSessions : s.ReturnedSessions + "|" + id.Stage + "|", Failure = failed };
                case StreamPhase.ValidateSessionResult:
                    Need(Has(s.ReturnedSessions, id.Stage) && !s.Failure, "SessionValidationBeforeSuccessfulFactory");
                    return s with { ValidatedSessions = failed ? s.ValidatedSessions : s.ValidatedSessions + "|" + id.Stage + "|", Failure = failed };
                case StreamPhase.CommitSession:
                    Need(Has(s.ValidatedSessions, id.Stage) && !s.Failure, "OwnershipTransferredBeforeNonNullSessionValidation");
                    return s with
                    {
                        Life = life with
                        {
                            LiveSessions = life.LiveSessions + "|" + id.Stage + "|",
                            Resolved = id.Stage < 0 ? life.Resolved : life.Resolved + "|" + id.Stage + "|"
                        }
                    };
                case StreamPhase.SourceFactory:
                case StreamPhase.WrapperFactory:
                    Need(s.Handler && !s.Enumerable && !s.Failure, "SourceFactoryBeforeResolveOrRepeated"); return s with { Enumerable = !failed, Failure = failed };
                case StreamPhase.AcquireSource:
                    Need(s.Enumerable && !life.Source && !s.Failure, "SourceAcquireWithoutEnumerableOrRepeated");
                    return s with { Life = life with { Source = !failed }, Failure = failed };
                case StreamPhase.MarkStarted:
                    Need(life.Source && !life.Started && !s.Failure, "StartedWithoutSuccessfulSourceAcquire");
                    Need(sessions.All(i => Has(life.LiveSessions, i)), "StartedBeforeSessionTransfers"); return s with { Life = life with { Started = true } };
                case StreamPhase.SourceGuard:
                    Need(life.Started && life.Source && !life.Ended && !life.Disposed && !s.Failure && !s.SourceGuard, "SourceGuardLifecycleMismatch");
                    return s with { SourceGuard = !failed, Failure = failed };
                case StreamPhase.SourceMove:
                    Need(s.SourceGuard && life.Started && life.Source && !s.Failure, "SourceMoveWithoutFreshCancellationGuard");
                    return s with
                    {
                        SourceGuard = false,
                        SourceCompleted = true,
                        SourceSuccess = outcome == StreamOutcome.True,
                        CurrentRead = false,
                        NextStage = 0,
                        GuardedStage = -1,
                        Cached = false,
                        Failure = failed
                    };
                case StreamPhase.SourceCurrent:
                    Need(s.SourceSuccess && !s.CurrentRead && !s.Failure, "SourceCurrentWithoutSuccessfulMoveOrRepeated");
                    return s with { CurrentRead = !failed, Failure = failed };
                case StreamPhase.HookGuard:
                    Need(s.CurrentRead && s.NextStage == id.Stage && s.GuardedStage == -1 && !s.Failure, "HookGuardBeforeCurrentOrOutOfOrder");
                    return s with { GuardedStage = failed ? -1 : id.Stage, Failure = failed };
                case StreamPhase.HookCached:
                    Need(s.GuardedStage == id.Stage && s.CurrentRead, "HookCacheReadWithoutCancellationGuard"); return s;
                case StreamPhase.ResolveHook:
                    Need(s.GuardedStage == id.Stage && !HasService(life, id.Stage) && !s.Failure, "HookResolveWithoutFreshGuardOrAlreadyCached");
                    return s with { Life = failed ? life : life with { Resolved = life.Resolved + "|" + id.Stage + "|" }, Failure = failed };
                case StreamPhase.Filter:
                case StreamPhase.Map:
                    Need(s.CurrentRead && s.NextStage == id.Stage && s.GuardedStage == id.Stage && HasService(life, id.Stage) && !s.Failure,
                        "ItemCallbackWithoutCurrentGuardResolveOrDeclarationOrder");
                    if (outcome == StreamOutcome.False) return s with { CurrentRead = false, SourceCompleted = false, SourceSuccess = false, NextStage = 0, GuardedStage = -1 };
                    return s with { NextStage = failed ? s.NextStage : id.Stage + 1, GuardedStage = -1, Failure = failed };
                case StreamPhase.CacheCurrent:
                    Need(s.CurrentRead && s.NextStage == contract.Hooks.Count && !s.Cached && !s.Failure, "CacheBeforeCompleteItemPipelineOrRepeated");
                    return s with { Cached = true, Life = life with { Cache = true } };
                case StreamPhase.StartupDetachLink:
                    Need(s.Failure && !life.Started && !life.Source && !s.StartupDetached, "StartupCleanupAfterStartedSourceOrWithoutFailure");
                    return s with { StartupDetached = true, DetachedLink = life.Link, Life = life with { Link = false } };
                case StreamPhase.StartupLinkPresent:
                    Need(s.StartupDetached, "StartupLinkCheckBeforeDetach");
                    Need(life.LiveSessions.Length == 0 && Cleaned(s), "LinkedTokenCleanupBeforeSessionsCompleted"); return s;
                case StreamPhase.StartupDisposeLink:
                    Need(s.StartupDetached && s.DetachedLink && !s.LinkCleaned, "StartupLinkCleanupWithoutOwnedDetachedResource");
                    return s with { LinkCleaned = true, Failure = true };
                case StreamPhase.EndFalse:
                    Need(s.SourceCompleted && !s.SourceSuccess && life.Started && !s.Failure, "NaturalEndWithoutSourceFalse");
                    return s with { EndMarked = true, Life = life with { Ended = true, Cache = false } };
                case StreamPhase.EndFailure:
                    Need(s.Failure && (life.Started || s.StartupDetached) && (!s.DetachedLink || s.LinkCleaned), "FailureExitBeforeStartupCleanup");
                    Need(life.Started || life.LiveSessions.Length == 0 && Cleaned(s), "StartupFailureLeakedSession");
                    return s with { EndMarked = true, Life = life with { Ended = true, Cache = false } };
                case StreamPhase.ReleaseTrue:
                    Need(s.Busy && s.Cached && !s.Failure && !life.Ended && !life.Disposed, "TrueReleaseWithoutAcceptedItem"); return s with { Busy = false };
                case StreamPhase.ReleaseFalse:
                    Need(s.Busy && !s.Failure && (life.Ended || life.Disposed) && !life.Cache, "FalseReleaseBeforeEnd"); return s with { Busy = false };
                case StreamPhase.ReleaseFailure:
                    Need(s.Busy && s.Failure && s.EndMarked && life.Ended, "FailureReleaseBeforeMarkEnded"); return s with { Busy = false };
                case StreamPhase.CurrentGuard: return s with { CurrentChecked = outcome == StreamOutcome.True };
                case StreamPhase.DisposeGuard:
                    Need(!s.Busy || failed, "PendingDisposeWasAccepted"); return failed ? s with { MisuseFailure = true } : s;
                case StreamPhase.AlreadyDisposed: return s with { DisposedChecked = true };
                case StreamPhase.MarkDisposed:
                    Need(s.DisposedChecked && !life.Disposed && !s.Busy && !s.DisposeBegan, "DisposeStartedBeforeGuardOrRepeated");
                    return s with { DisposeBegan = true, Busy = true, Life = life with { Disposed = true, Cache = false } };
                case StreamPhase.DetachSource:
                    Need(s.DisposeBegan && life.Disposed && !s.SourceDetached, "SourceDetachBeforeDisposeOrRepeated");
                    return s with { SourceDetached = true, DetachedSource = life.Source, Life = life with { Source = false } };
                case StreamPhase.DetachLink:
                    Need(s.SourceDetached && !s.LinkDetached, "LinkDetachBeforeSourceDetachOrRepeated");
                    return s with { LinkDetached = true, DetachedLink = life.Link, Life = life with { Link = false } };
                case StreamPhase.SourcePresent: Need(s.SourceDetached && s.LinkDetached, "CleanupBeforeAllOwnersDetached"); return s;
                case StreamPhase.DetachSession:
                    Need(id.Program == StreamProgram.Move ? s.StartupDetached && !life.Started : s.SourceDetached && s.LinkDetached && s.DisposeBegan,
                        "SessionDetachOutsideStartupFailureOrDispose");
                    return s with
                    {
                        DetachedSessions = Has(life.LiveSessions, id.Stage) ? s.DetachedSessions + "|" + id.Stage + "|" : s.DetachedSessions,
                        Life = life with { LiveSessions = life.LiveSessions.Replace("|" + id.Stage + "|", "") }
                    };
                case StreamPhase.SessionPresent:
                    Need(life.LiveSessions.Length == 0, "SessionCleanupBeforeAllLeasesDetached"); return s;
                case StreamPhase.CleanupSession:
                    var next = sessions.Where(i => Sessions(contract, i)[0].Ownership == FusionOwnership.OwnedSession &&
                        Has(s.DetachedSessions, i) && !Has(s.CleanedSessions, i)).DefaultIfEmpty(int.MinValue).First();
                    Need(life.LiveSessions.Length == 0 && !life.Source && !life.Link && Has(s.DetachedSessions, id.Stage) && next == id.Stage &&
                        Sessions(contract, id.Stage)[0].Ownership == FusionOwnership.OwnedSession, "SessionCleanupOwnershipOrderOrDetachMismatch");
                    Need(id.Program == StreamProgram.Move || !s.DetachedSource || s.SourceCleaned, "SessionCleanupBeforeSourceDisposeCompleted");
                    return s with { CleanedSessions = s.CleanedSessions + "|" + id.Stage + "|", Failure = s.Failure || failed };
                case StreamPhase.DisposeSource:
                    Need(s.SourceDetached && s.LinkDetached && s.DetachedSource && !s.SourceCleaned && s.Busy, "SourceCleanupWithoutDetachedOwnerOrRepeated");
                    Need(life.LiveSessions.Length == 0, "UserCleanupBeforeAllSessionLeasesDetached");
                    return s with { SourceCleaned = true, Failure = s.Failure || failed };
                case StreamPhase.LinkPresent:
                    Need(s.SourceDetached && s.LinkDetached && (!s.DetachedSource || s.SourceCleaned), "LinkCleanupBeforeSourceCompleted");
                    Need(Cleaned(s), "LinkedTokenCleanupBeforeSessionsCompleted"); return s;
                case StreamPhase.DisposeLink:
                    Need(s.LinkDetached && s.DetachedLink && (!s.DetachedSource || s.SourceCleaned) && !s.LinkCleaned && s.Busy, "LinkCleanupWithoutDetachedOwnerOrBeforeSource");
                    Need(Cleaned(s), "LinkedTokenCleanupBeforeSessionsCompleted");
                    return s with { LinkCleaned = true, Failure = s.Failure || failed };
                case StreamPhase.ReleaseDispose:
                    Need(s.DisposeBegan && s.Busy && (!s.DetachedSource || s.SourceCleaned) && (!s.DetachedLink || s.LinkCleaned), "DisposeReleasedBeforeAllCleanupCompleted");
                    Need(life.LiveSessions.Length == 0 && Cleaned(s), "DisposeReleasedBeforeSessionCleanupCompleted");
                    return s with { Busy = false };
                case StreamPhase.DisposeResult:
                    Need(!s.Busy && s.DisposeBegan, "DisposeResultBeforeCleanupRelease"); return s;
                default: Reject("UnknownStreamControlPhase"); return s;
            }
        }
        void CheckExit(StreamSiteId id, Path s)
        {
            var life = s.Life;
            bool valid = id.Phase switch
            {
                StreamPhase.NullFailure => s.Failure && !s.EnvelopeCreated,
                StreamPhase.EnvelopeReturn => s.EnvelopeCreated && s.NullChecked,
                StreamPhase.CursorReturn => s.CursorCreated && !life.Started && !life.Source && !life.Link,
                StreamPhase.MoveTrue => !s.Busy && s.Cached && life.Cache && !s.Failure && !life.Ended && !life.Disposed,
                StreamPhase.MoveFalse => !s.Busy && (life.Ended || life.Disposed) && !life.Cache && !s.Failure,
                StreamPhase.MoveFailure => !s.Busy && s.Failure && life.Ended && s.EndMarked,
                StreamPhase.BusyFailure => s.Busy && s.MisuseFailure,
                StreamPhase.ReadCache => s.CurrentChecked && life.Cache && !s.Busy && !life.Ended && !life.Disposed,
                StreamPhase.CurrentFailure => !s.CurrentChecked && (!life.Cache || s.Busy || life.Ended || life.Disposed),
                StreamPhase.DisposeSuccess => !s.Busy && life.Disposed && !life.Source && !life.Link && !s.Failure && s.DisposedChecked,
                StreamPhase.DisposeFailure => !s.Busy && life.Disposed && !life.Source && !life.Link && s.Failure && s.DisposeBegan,
                _ => false
            };
            if (!valid) Reject("InvalidStreamExit:" + id.Phase);
            if (id.Program == StreamProgram.Move && life.Started && !life.Disposed && (!life.Source || (s.DetachedLink && !life.Link))) Reject("StartedMoveReleasedCursorBeforeDispose");
            if (id.Program == StreamProgram.Move && !life.Started && (life.Source || life.Link)) Reject("StartupFailureLeakedPartialOwner");
            if (id.Program == StreamProgram.Move && life.Started && !life.Disposed && sessions.Any(i => !Has(life.LiveSessions, i))) Reject("StartedMoveReleasedSessionBeforeDispose");
            if (id.Program == StreamProgram.Move && !life.Started && (life.LiveSessions.Length != 0 || !Cleaned(s))) Reject("StartupFailureLeakedSession");
        }
    }
    private static bool HasService(Lifecycle life, int stage) => life.Resolved.Contains("|" + stage + "|");
    private static StreamSiteId Entry(StreamProgram program) => Id(program, program switch
    {
        StreamProgram.Create => StreamPhase.NullGuard,
        StreamProgram.GetEnumerator => StreamPhase.CreateCursor,
        StreamProgram.Move => StreamPhase.MoveGuard,
        StreamProgram.Current => StreamPhase.CurrentGuard,
        _ => StreamPhase.DisposeGuard
    });
    private static StreamOutcome[] AllOutcomes(StreamPhase phase) => phase switch
    {
        StreamPhase.NullFailure or StreamPhase.EnvelopeReturn or StreamPhase.CursorReturn or StreamPhase.MoveTrue or StreamPhase.MoveFalse or
            StreamPhase.MoveFailure or StreamPhase.BusyFailure or StreamPhase.ReadCache or StreamPhase.CurrentFailure or
            StreamPhase.DisposeSuccess or StreamPhase.DisposeFailure => Array.Empty<StreamOutcome>(),
        StreamPhase.Stopped or StreamPhase.Started or StreamPhase.HookCached or StreamPhase.StartupLinkPresent or StreamPhase.CurrentGuard or
            StreamPhase.AlreadyDisposed or StreamPhase.SourcePresent or StreamPhase.LinkPresent or StreamPhase.DisposeResult => new[] { StreamOutcome.True, StreamOutcome.False },
        StreamPhase.SessionPresent => new[] { StreamOutcome.True, StreamOutcome.False },
        StreamPhase.SourceMove or StreamPhase.Filter => new[] { StreamOutcome.True, StreamOutcome.False, StreamOutcome.Failure },
        StreamPhase.NullGuard or StreamPhase.MoveGuard or StreamPhase.MergeTokens or StreamPhase.PreCancel or StreamPhase.ResolveHandler or StreamPhase.ResolveWrapper or StreamPhase.SourceFactory or StreamPhase.WrapperFactory or
            StreamPhase.AcquireSource or StreamPhase.SourceGuard or StreamPhase.SourceCurrent or StreamPhase.HookGuard or StreamPhase.ResolveHook or StreamPhase.Map or
            StreamPhase.StartupDisposeLink or StreamPhase.DisposeGuard or StreamPhase.DisposeSource or StreamPhase.DisposeLink or
            StreamPhase.SessionGuard or StreamPhase.ResolveSessionFactory or StreamPhase.AcquireSession or StreamPhase.ValidateSessionResult or StreamPhase.CleanupSession => new[] { StreamOutcome.Success, StreamOutcome.Failure },
        _ => new[] { StreamOutcome.Always }
    };
}
