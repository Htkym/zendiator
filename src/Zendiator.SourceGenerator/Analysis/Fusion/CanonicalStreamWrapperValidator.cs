using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamWrapperValidator
{
    private sealed record State(bool Begun = false, int Opens = 0, bool OpenAllowed = false, bool OutputAllowed = false,
        bool Guarded = false, bool RequestSaved = false, bool TokenSaved = false, bool Acquired = false,
        bool Assigned = false, bool ReleaseStarted = false, bool EndRequested = false, bool Detached = false,
        bool Disposed = false, bool Failure = false, bool MoveAllowed = false);
    private sealed record Visit(StreamWrapperSite Site, State State);
    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(BoundPipelineModel route, CanonicalStreamWrapperSchedule candidate)
    {
        var initial = CanonicalStreamGraphValidator.CheckContract(route);
        if (initial.Count != 0) return initial;
        var facts = new HashSet<CanonicalExecutionGraphFact>();
        void Reject(string reason) => facts.Add(new(reason, route.Anchor));
        var required = RequiredStreamWrapperContract.Derive(route);
        if (required.Boundaries.Count == 0) Reject("StreamWrapperRequired");
        Compare(required.Segments, candidate.Segments.Select(s => s.Descriptor), "Segment");
        if (candidate.ConsumerSegment != required.Segments.Last().Id) Reject("ConsumerSegmentMismatch");
        if (candidate.ContextModelId != "stream-call-caller-v1") Reject("FreshMoveDisposeCallerContextRequired");
        foreach (var segment in candidate.Segments)
        {
            var declaration = required.Segments.SingleOrDefault(s => s.Id == segment.Descriptor.Id);
            if (declaration != null) facts.UnionWith(CanonicalStreamGraphValidator.ValidateSegment(declaration.Contract, segment.Cursor));
        }
        if (candidate.Wrappers.Select(w => w.Position).Distinct().Count() != candidate.Wrappers.Count) Reject("DuplicateWrapperProtocol");
        foreach (var expected in required.Boundaries)
        {
            var found = candidate.Wrappers.Where(w => w.Position == expected.Position).ToArray();
            if (found.Length != 1) { Reject("MissingWrapperProtocol"); continue; }
            var protocol = found[0];
            if (protocol.InputSegment != expected.Input || protocol.OutputSegment != expected.Output) Reject("WrapperBoundaryMismatch");
            if (protocol.OpenCalls != StreamMultiplicity.ZeroOrMore || protocol.EnumerationsPerOpen != StreamMultiplicity.ZeroOrMore ||
                !protocol.LazyOpen || !protocol.FreshEnvelopeEachOpen || !protocol.FreshCursorEachAcquisition) Reject("IndependentLazyOpenMultiplicityRequired");
            if (!protocol.BodyOpaque || protocol.BodyCleanupProven || protocol.InsertAsyncAdapter) Reject("OpaqueWrapperBodyBoundaryMismatch");
            if (protocol.OpenAvailability != "wrapper-call-or-enumeration") Reject("OpenRestrictedToSynchronousFactory");
            Compare(expected.Transfers, protocol.Transfers, "WrapperTransfer");
            Compare(expected.Owners, protocol.Owners, "WrapperCursorOwner");
            Compare(expected.Awaits, protocol.Awaits, "WrapperAwait");
            Compare(expected.Calls, protocol.Calls, "WrapperCall");
            if (protocol.Owners.Any(o => o.BorrowedService)) Reject("BorrowedServiceCannotBeOwnedCursor");
            if (protocol.Awaits.Any(a => !a.ConsumeOnce || !a.ConfigureAwaitFalse || !a.CompleteBeforeOwnerExit || !a.PreserveIdentityAndToken || !a.EscapingOceIsCanceled)) Reject("WrapperAwaitConsumptionOrFailureMismatch");
            ValidatePaths(expected, protocol);
        }
        if (candidate.Wrappers.Any(w => !required.Boundaries.Any(r => r.Position == w.Position))) Reject("UnexpectedWrapperProtocol");
        return new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal));

        void Compare<T>(IEnumerable<T> expected, IEnumerable<T> actual, string kind)
        {
            var remaining = expected.ToList();
            foreach (var value in actual)
            {
                var i = remaining.FindIndex(v => EqualityComparer<T>.Default.Equals(value, v));
                if (i < 0) Reject("Unexpected" + kind); else remaining.RemoveAt(i);
            }
            if (remaining.Count != 0) Reject("Missing" + kind);
        }
        void ValidatePaths(RequiredStreamWrapperBoundary expected, StreamWrapperProtocol protocol)
        {
            var reached = new HashSet<StreamWrapperSite>();
            foreach (var entry in expected.Entries)
            {
                var matches = protocol.Flows.Where(f => f.Program == entry.Key).ToArray();
                if (matches.Length != 1) { Reject("MissingOrDuplicateWrapperFlow"); continue; }
                var flow = matches[0];
                if (flow.Entry != entry.Value) { Reject("WrapperEntryMismatch"); continue; }
                if (flow.Edges.Any(e => e.From.Program != flow.Program || e.To.Program != flow.Program ||
                    !expected.Sites.Contains(e.From) || !expected.Sites.Contains(e.To))) { Reject("UnknownOrCrossWrapperProgramSite"); continue; }
                if (flow.Edges.GroupBy(e => (e.From, e.Outcome)).Any(g => g.Count() != 1)) { Reject("AmbiguousWrapperOutcome"); continue; }
                Walk(flow, false);
                if (flow.Program is StreamWrapperProgram.ReleaseByWrapper or StreamWrapperProgram.ReleaseByConsumer) Walk(flow, true);
            }
            if (protocol.Flows.Any(f => !expected.Entries.ContainsKey(f.Program))) Reject("UnexpectedWrapperProgram");
            foreach (var site in expected.Sites.Where(s => !reached.Contains(s))) Reject("UnreachableWrapperSite:" + site.Phase);

            void Walk(StreamWrapperFlow flow, bool originalError)
            {
                var queue = new Queue<Visit>(); var seen = new HashSet<Visit>();
                queue.Enqueue(new(flow.Entry, new(Failure: originalError)));
                var adjacency = flow.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToArray());
                var exits = 0;
                while (queue.Count != 0)
                {
                    var visit = queue.Dequeue(); if (!seen.Add(visit)) continue;
                    reached.Add(visit.Site); var s = visit.State; var id = visit.Site;
                    var outcomes = Outcomes(id, s);
                    if (outcomes.Length == 0)
                    {
                        exits++;
                        if (!ExitValid(id, s)) Reject("InvalidWrapperExit:" + id.Phase);
                        if (adjacency.ContainsKey(id)) Reject("ExecutionAfterWrapperReturn");
                        continue;
                    }
                    foreach (var outcome in outcomes)
                    {
                        var edges = adjacency.TryGetValue(id, out var found) ? found.Where(e => e.Outcome == outcome).ToArray() : Array.Empty<StreamWrapperEdge>();
                        if (edges.Length != 1) { Reject("MissingWrapperOutcome:" + id.Phase + ":" + outcome); continue; }
                        var valid = true;
                        void Need(bool value, string reason) { if (!value) { Reject(reason); valid = false; } }
                        var failed = outcome == StreamWrapperOutcome.Failure;
                        var next = s;
                        switch (id.Phase)
                        {
                            case StreamWrapperPhase.Begin: Need(!s.Begun, "WrapperBeginRepeated"); next = s with { Begun = true }; break;
                            case StreamWrapperPhase.OpenChoice:
                                Need(s.Begun && !s.OutputAllowed, "OpenChoiceOutsideOpaqueInvocation");
                                next = s with { OpenAllowed = outcome == StreamWrapperOutcome.More, OutputAllowed = outcome == StreamWrapperOutcome.Zero }; break;
                            case StreamWrapperPhase.CallOpen:
                                Need(s.OpenAllowed && !s.OutputAllowed, "OpenCallWithoutOpaqueChoice");
                                next = s with { OpenAllowed = false, Opens = failed ? s.Opens : Math.Min(2, s.Opens + 1) }; break;
                            case StreamWrapperPhase.OutputChoice:
                                Need(s.OutputAllowed && !s.OpenAllowed, "WrapperOutputBeforeOpaqueCallsComplete");
                                next = s with { Failure = failed }; break;
                            case StreamWrapperPhase.NullGuard: next = s with { Guarded = !failed, Failure = failed }; break;
                            case StreamWrapperPhase.SaveRequest:
                                Need(s.Guarded && !s.RequestSaved && !s.Failure, "OpenRequestSavedBeforeGuardOrRepeated"); next = s with { RequestSaved = true }; break;
                            case StreamWrapperPhase.SaveToken:
                                Need(s.RequestSaved && !s.TokenSaved && !s.Failure, "OpenTokenSavedBeforeSelectedRequestOrRepeated"); next = s with { TokenSaved = true }; break;
                            case StreamWrapperPhase.AcquireCursor:
                                Need(!s.Acquired && !s.Assigned, "UpstreamCursorAcquiredTwiceInOneAcquisition"); next = s with { Acquired = !failed, Failure = failed }; break;
                            case StreamWrapperPhase.AssignOwner:
                                Need(s.Acquired && !s.Assigned && !s.Failure, "CursorOwnerBeforeSuccessfulAcquisition"); next = s with { Assigned = true }; break;
                            case StreamWrapperPhase.OwnedCursorEntry:
                                Need(!s.ReleaseStarted, "OwnedCursorReleaseReentered"); next = s with { ReleaseStarted = true, Acquired = true, Assigned = true }; break;
                            case StreamWrapperPhase.UseChoice:
                                Need(s.ReleaseStarted && s.Assigned && !s.Detached && !s.Disposed && !s.MoveAllowed, "CursorUsedOutsideItsOwnerLifetime");
                                next = s with { MoveAllowed = outcome == StreamWrapperOutcome.Continue, EndRequested = outcome == StreamWrapperOutcome.Finish, Failure = outcome == StreamWrapperOutcome.Recover ? false : s.Failure }; break;
                            case StreamWrapperPhase.MoveUpstream:
                                Need(s.ReleaseStarted && s.Assigned && !s.Detached && !s.EndRequested && !s.Disposed && s.MoveAllowed && !s.Failure, "UpstreamMoveAfterDetachOrOutsideOwnedLifetime");
                                next = s with { MoveAllowed = false, Failure = failed }; break;
                            case StreamWrapperPhase.DetachCursor:
                                Need(s.Assigned && s.EndRequested && !s.Detached && !s.Disposed, "CursorDetachBeforeOwnerEndOrRepeated"); next = s with { Detached = true, Assigned = false }; break;
                            case StreamWrapperPhase.DisposeCursor:
                                Need(s.ReleaseStarted && s.Detached && !s.Assigned && !s.Disposed && s.EndRequested, "CursorCleanupBeforeDetachOrRepeated");
                                next = s with { Disposed = true, Failure = s.Failure || failed }; break;
                            case StreamWrapperPhase.ReleaseResult: Need(s.Disposed && s.Detached && !s.Assigned, "OwnerExitBeforeCursorCleanupCompleted"); break;
                            default: Reject("UnknownWrapperPhase"); valid = false; break;
                        }
                        if (valid) queue.Enqueue(new(edges[0].To, next));
                    }
                    if (adjacency.TryGetValue(id, out var actual) && actual.Any(e => !AllOutcomes(id.Phase).Contains(e.Outcome))) Reject("UnexpectedWrapperOutcome:" + id.Phase);
                }
                if (exits == 0) Reject("WrapperFlowHasNoExit");
            }
        }
    }
    private static StreamWrapperOutcome[] Outcomes(StreamWrapperSite id, State s) => id.Phase switch {
        StreamWrapperPhase.OutputChoice => s.Opens == 0 ? new[] { StreamWrapperOutcome.Alternative, StreamWrapperOutcome.Failure } : AllOutcomes(id.Phase),
        StreamWrapperPhase.UseChoice => s.Failure ? new[] { StreamWrapperOutcome.Finish, StreamWrapperOutcome.Recover } : new[] { StreamWrapperOutcome.Continue, StreamWrapperOutcome.Finish },
        StreamWrapperPhase.ReleaseResult => new[] { s.Failure ? StreamWrapperOutcome.Failure : StreamWrapperOutcome.Success },
        _ => AllOutcomes(id.Phase)
    };
    private static bool ExitValid(StreamWrapperSite id, State s) => id.Phase switch {
        StreamWrapperPhase.DirectReturn => s.Begun && s.OutputAllowed && s.Opens > 0 && !s.OpenAllowed && !s.Acquired && !s.Failure,
        StreamWrapperPhase.AlternativeReturn => s.Begun && s.OutputAllowed && !s.OpenAllowed && !s.Acquired && !s.Failure,
        StreamWrapperPhase.WrapperFailure => s.Begun && s.OutputAllowed && s.Failure,
        StreamWrapperPhase.ArgumentFailure => !s.Guarded && s.Failure && !s.RequestSaved && !s.TokenSaved,
        StreamWrapperPhase.EnumerableReturn => s.Guarded && s.RequestSaved && s.TokenSaved && !s.Acquired && !s.Failure,
        StreamWrapperPhase.CursorReturn => s.Acquired && s.Assigned && !s.Failure,
        StreamWrapperPhase.AcquireFailure => !s.Acquired && !s.Assigned && s.Failure,
        StreamWrapperPhase.ReleaseSuccess => s.ReleaseStarted && s.Detached && s.Disposed && !s.Assigned && !s.Failure,
        StreamWrapperPhase.ReleaseFailure => s.ReleaseStarted && s.Detached && s.Disposed && !s.Assigned && s.Failure,
        _ => false
    };
    private static StreamWrapperOutcome[] AllOutcomes(StreamWrapperPhase phase) => phase switch {
        StreamWrapperPhase.DirectReturn or StreamWrapperPhase.AlternativeReturn or StreamWrapperPhase.WrapperFailure or StreamWrapperPhase.ArgumentFailure or
            StreamWrapperPhase.EnumerableReturn or StreamWrapperPhase.CursorReturn or StreamWrapperPhase.AcquireFailure or StreamWrapperPhase.ReleaseSuccess or StreamWrapperPhase.ReleaseFailure => Array.Empty<StreamWrapperOutcome>(),
        StreamWrapperPhase.OpenChoice => new[] { StreamWrapperOutcome.Zero, StreamWrapperOutcome.More },
        StreamWrapperPhase.OutputChoice => new[] { StreamWrapperOutcome.Direct, StreamWrapperOutcome.Alternative, StreamWrapperOutcome.Failure },
        StreamWrapperPhase.UseChoice => new[] { StreamWrapperOutcome.Continue, StreamWrapperOutcome.Finish, StreamWrapperOutcome.Recover },
        StreamWrapperPhase.CallOpen or StreamWrapperPhase.NullGuard or StreamWrapperPhase.AcquireCursor or StreamWrapperPhase.MoveUpstream or
            StreamWrapperPhase.DisposeCursor or StreamWrapperPhase.ReleaseResult => new[] { StreamWrapperOutcome.Success, StreamWrapperOutcome.Failure },
        _ => new[] { StreamWrapperOutcome.Always }
    };
}
