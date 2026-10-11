using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.StreamWrapperNames;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamWrapperBuilder
{
    internal static CanonicalStreamWrapperSchedule Build(BoundPipelineModel route)
    {
        var segments = new List<StreamExecutionSegment>();
        var protocols = new List<StreamWrapperProtocol>();
        var hooks = new List<int>();
        var sourcePosition = -1;
        string? upstream = null;
        for (var position = 0; position < route.Stages.Count; position++)
        {
            if (route.Stages[position].Kind != FusionStageKind.Wrapper) { hooks.Add(position); continue; }
            Flush();
            upstream = Segment(sourcePosition);
            protocols.Add(Protocol(position, upstream, Segment(position)));
            sourcePosition = position;
            hooks.Clear();
        }
        Flush();
        return new(new(segments), new(protocols), Segment(sourcePosition), "stream-call-caller-v1");
        void Flush()
        {
            var source = sourcePosition < 0 ? route.Handlers[0].Callback! : route.Stages[sourcePosition].Callbacks[0];
            var contract = new StreamSegmentContract(route.Anchor, sourcePosition < 0 ? StreamSourceKind.Handler : StreamSourceKind.Wrapper,
                source, new(hooks.Select(i => route.Stages[i])));
            var descriptor = new StreamSegmentDescriptor(Segment(sourcePosition), sourcePosition, new(hooks), upstream, contract);
            segments.Add(new(descriptor, CanonicalStreamScheduleBuilder.Build(contract)));
        }
    }

    private static StreamWrapperProtocol Protocol(int position, string input, string output)
    {
        var flows = new List<StreamWrapperFlow>();
        var transfers = new List<StreamWrapperTransfer>();
        var awaits = new List<StreamWrapperAwait>();
        var owners = new List<StreamWrapperCursorOwner>();
        var calls = new List<StreamWrapperCall> { new(Site(StreamWrapperProgram.Invoke, StreamWrapperPhase.CallOpen), StreamWrapperProgram.Open, null, null, StreamMultiplicity.EachOpen) };
        Flow(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, new[] {
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, StreamWrapperPhase.OpenChoice),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.OpenChoice, StreamWrapperPhase.OutputChoice, StreamWrapperOutcome.Zero),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.OpenChoice, StreamWrapperPhase.CallOpen, StreamWrapperOutcome.More),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.CallOpen, StreamWrapperPhase.OpenChoice, StreamWrapperOutcome.Success),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.CallOpen, StreamWrapperPhase.OpenChoice, StreamWrapperOutcome.Failure),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.OutputChoice, StreamWrapperPhase.DirectReturn, StreamWrapperOutcome.Direct),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.OutputChoice, StreamWrapperPhase.AlternativeReturn, StreamWrapperOutcome.Alternative),
            E(StreamWrapperProgram.Invoke, StreamWrapperPhase.OutputChoice, StreamWrapperPhase.WrapperFailure, StreamWrapperOutcome.Failure)
        });
        Flow(StreamWrapperProgram.Open, StreamWrapperPhase.NullGuard, new[] {
            E(StreamWrapperProgram.Open, StreamWrapperPhase.NullGuard, StreamWrapperPhase.SaveRequest, StreamWrapperOutcome.Success),
            E(StreamWrapperProgram.Open, StreamWrapperPhase.NullGuard, StreamWrapperPhase.ArgumentFailure, StreamWrapperOutcome.Failure),
            E(StreamWrapperProgram.Open, StreamWrapperPhase.SaveRequest, StreamWrapperPhase.SaveToken),
            E(StreamWrapperProgram.Open, StreamWrapperPhase.SaveToken, StreamWrapperPhase.EnumerableReturn)
        });
        Transfer(StreamWrapperProgram.Open, StreamWrapperPhase.SaveRequest, "Open/request-argument", input + "/envelope/request");
        Transfer(StreamWrapperProgram.Open, StreamWrapperPhase.SaveToken, "Open/token-argument", input + "/envelope/api-token");
        // Outer callbacks keep outer state even when an opaque Wrapper chooses different upstream arguments.
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, output + "/cursor/request", "Wrap/request");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, output + "/cursor/start-token", "Wrap/token");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.DirectReturn, "Open/returned-enumerable", output + "/source-enumerable");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.AlternativeReturn, "Wrap/opaque-output", output + "/source-enumerable");
        for (var consumer = 0; consumer < 2; consumer++)
        {
            var who = (StreamCursorConsumer)consumer;
            var acquire = who == StreamCursorConsumer.Wrapper ? StreamWrapperProgram.AcquireByWrapper : StreamWrapperProgram.AcquireByConsumer;
            var release = who == StreamCursorConsumer.Wrapper ? StreamWrapperProgram.ReleaseByWrapper : StreamWrapperProgram.ReleaseByConsumer;
            owners.Add(new(who, StreamMultiplicity.EachAcquiredCursor, who == StreamCursorConsumer.Wrapper ? "wrapper-call-or-enumeration" : "consumer-enumeration", true, true, true, false));
            Flow(acquire, StreamWrapperPhase.AcquireCursor, new[] {
                E(acquire, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AssignOwner, StreamWrapperOutcome.Success),
                E(acquire, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AcquireFailure, StreamWrapperOutcome.Failure),
                E(acquire, StreamWrapperPhase.AssignOwner, StreamWrapperPhase.CursorReturn)
            });
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, input + "/envelope/request", input + "/cursor/request", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, input + "/envelope/api-token", input + "/cursor/api-token", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, who == StreamCursorConsumer.Wrapper ? "Wrapper/enumerator-token-argument" : "Consumer/enumerator-token-argument",
                input + "/cursor/enumerator-token", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AssignOwner, "acquired/upstream-cursor", who + "/owned-cursor");
            calls.Add(new(Site(acquire, StreamWrapperPhase.AcquireCursor), null, input, StreamProgram.GetEnumerator, StreamMultiplicity.EachAcquiredCursor));
            calls.Add(new(Site(release, StreamWrapperPhase.MoveUpstream), null, input, StreamProgram.Move, StreamMultiplicity.ZeroOrMore));
            calls.Add(new(Site(release, StreamWrapperPhase.DisposeCursor), null, input, StreamProgram.Dispose, StreamMultiplicity.EachAcquiredCursor));
            Flow(release, StreamWrapperPhase.OwnedCursorEntry, new[] {
                E(release, StreamWrapperPhase.OwnedCursorEntry, StreamWrapperPhase.UseChoice),
                E(release, StreamWrapperPhase.UseChoice, StreamWrapperPhase.MoveUpstream, StreamWrapperOutcome.Continue),
                E(release, StreamWrapperPhase.UseChoice, StreamWrapperPhase.DetachCursor, StreamWrapperOutcome.Finish),
                E(release, StreamWrapperPhase.UseChoice, StreamWrapperPhase.UseChoice, StreamWrapperOutcome.Recover),
                E(release, StreamWrapperPhase.MoveUpstream, StreamWrapperPhase.UseChoice, StreamWrapperOutcome.Success),
                E(release, StreamWrapperPhase.MoveUpstream, StreamWrapperPhase.UseChoice, StreamWrapperOutcome.Failure),
                E(release, StreamWrapperPhase.DetachCursor, StreamWrapperPhase.DisposeCursor),
                E(release, StreamWrapperPhase.DisposeCursor, StreamWrapperPhase.ReleaseResult, StreamWrapperOutcome.Success),
                E(release, StreamWrapperPhase.DisposeCursor, StreamWrapperPhase.ReleaseResult, StreamWrapperOutcome.Failure),
                E(release, StreamWrapperPhase.ReleaseResult, StreamWrapperPhase.ReleaseSuccess, StreamWrapperOutcome.Success),
                E(release, StreamWrapperPhase.ReleaseResult, StreamWrapperPhase.ReleaseFailure, StreamWrapperOutcome.Failure)
            });
            Transfer(release, StreamWrapperPhase.DetachCursor, who + "/owned-cursor", who + "/dispose-cursor");
            Transfer(release, StreamWrapperPhase.DisposeCursor, "cleanup/error", who + "/escaping-error", StreamWrapperOutcome.Failure);
            Transfer(release, StreamWrapperPhase.UseChoice, "opaque-owner/recovery", who + "/escaping-error", StreamWrapperOutcome.Recover);
            awaits.Add(new(Site(release, StreamWrapperPhase.MoveUpstream), "bool", true, true, true, true, true, false));
            awaits.Add(new(Site(release, StreamWrapperPhase.DisposeCursor), "void", true, true, true, true, true, true));
        }
        // The framework's delegated output acquisition has no implicit cancellation argument.
        // Keep explicit Wrapper/consumer GetEnumerator calls in their existing separate programs.
        var delegated = StreamWrapperProgram.AcquireDelegatedOutput;
        Flow(delegated, StreamWrapperPhase.AcquireCursor, new[] {
            E(delegated, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AssignOwner, StreamWrapperOutcome.Success),
            E(delegated, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AcquireFailure, StreamWrapperOutcome.Failure),
            E(delegated, StreamWrapperPhase.AssignOwner, StreamWrapperPhase.CursorReturn)
        });
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, input + "/envelope/request", input + "/cursor/request", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, input + "/envelope/api-token", input + "/cursor/api-token", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, output + "/source/token", input + "/cursor/enumerator-token", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AssignOwner, "acquired/upstream-cursor", "Consumer/owned-cursor");
        calls.Add(new(Site(delegated, StreamWrapperPhase.AcquireCursor), null, input, StreamProgram.GetEnumerator, StreamMultiplicity.EachAcquiredCursor));
        return new(position, input, output, StreamMultiplicity.ZeroOrMore, StreamMultiplicity.ZeroOrMore,
            true, true, true, true, false, false, new(flows), new(transfers), new(owners), new(awaits)) { Calls = new(calls) };
        void Flow(StreamWrapperProgram program, StreamWrapperPhase entry, IEnumerable<StreamWrapperEdge> edges) => flows.Add(new(program, Site(program, entry), new(edges)));
        void Transfer(StreamWrapperProgram program, StreamWrapperPhase phase, string from, string to, StreamWrapperOutcome outcome = StreamWrapperOutcome.Always) => transfers.Add(new(from, to, Site(program, phase), outcome));
    }
    private static StreamWrapperEdge E(StreamWrapperProgram program, StreamWrapperPhase from, StreamWrapperPhase to, StreamWrapperOutcome outcome = StreamWrapperOutcome.Always) => new(Site(program, from), Site(program, to), outcome);
}
