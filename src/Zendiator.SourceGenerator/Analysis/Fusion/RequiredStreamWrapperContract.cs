using System.Collections.Generic;
using System.Linq;
using static Zendiator.SourceGenerator.StreamWrapperNames;

namespace Zendiator.SourceGenerator;

internal sealed class RequiredStreamWrapperContract
{
    internal readonly List<StreamSegmentDescriptor> Segments = new();
    internal readonly List<RequiredStreamWrapperBoundary> Boundaries = new();
    internal static RequiredStreamWrapperContract Derive(BoundPipelineModel route)
    {
        var required = new RequiredStreamWrapperContract();
        var sources = new[] { -1 }.Concat(Enumerable.Range(0, route.Stages.Count).Where(i => route.Stages[i].Kind == FusionStageKind.Wrapper)).ToArray();
        for (var n = 0; n < sources.Length; n++)
        {
            var source = sources[n]; var end = n + 1 < sources.Length ? sources[n + 1] : route.Stages.Count;
            var hooks = Enumerable.Range(source + 1, end - source - 1).ToArray();
            var contract = new StreamSegmentContract(route.Anchor, source < 0 ? StreamSourceKind.Handler : StreamSourceKind.Wrapper,
                source < 0 ? route.Handlers[0].Callback! : route.Stages[source].Callbacks[0], new(hooks.Select(i => route.Stages[i])));
            required.Segments.Add(new(Segment(source), source, new(hooks), n == 0 ? null : Segment(sources[n - 1]), contract));
            if (n != 0) required.Boundaries.Add(DeriveBoundary(source, Segment(sources[n - 1]), Segment(source)));
        }
        return required;
    }
    private static RequiredStreamWrapperBoundary DeriveBoundary(int position, string input, string output)
    {
        var required = new RequiredStreamWrapperBoundary(position, input, output);
        required.Calls.Add(new(Site(StreamWrapperProgram.Invoke, StreamWrapperPhase.CallOpen), StreamWrapperProgram.Open, null, null, StreamMultiplicity.EachOpen));
        Add(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, StreamWrapperPhase.OpenChoice, StreamWrapperPhase.CallOpen,
            StreamWrapperPhase.OutputChoice, StreamWrapperPhase.DirectReturn, StreamWrapperPhase.AlternativeReturn, StreamWrapperPhase.WrapperFailure);
        Add(StreamWrapperProgram.Open, StreamWrapperPhase.NullGuard, StreamWrapperPhase.ArgumentFailure, StreamWrapperPhase.SaveRequest,
            StreamWrapperPhase.SaveToken, StreamWrapperPhase.EnumerableReturn);
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, output + "/cursor/request", "Wrap/request");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, output + "/cursor/start-token", "Wrap/token");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.DirectReturn, "Open/returned-enumerable", output + "/source-enumerable");
        Transfer(StreamWrapperProgram.Invoke, StreamWrapperPhase.AlternativeReturn, "Wrap/opaque-output", output + "/source-enumerable");
        Transfer(StreamWrapperProgram.Open, StreamWrapperPhase.SaveRequest, "Open/request-argument", input + "/envelope/request");
        Transfer(StreamWrapperProgram.Open, StreamWrapperPhase.SaveToken, "Open/token-argument", input + "/envelope/api-token");
        foreach (var who in new[] { StreamCursorConsumer.Wrapper, StreamCursorConsumer.Consumer })
        {
            var acquire = who == StreamCursorConsumer.Wrapper ? StreamWrapperProgram.AcquireByWrapper : StreamWrapperProgram.AcquireByConsumer;
            var release = who == StreamCursorConsumer.Wrapper ? StreamWrapperProgram.ReleaseByWrapper : StreamWrapperProgram.ReleaseByConsumer;
            Add(acquire, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AssignOwner, StreamWrapperPhase.CursorReturn, StreamWrapperPhase.AcquireFailure);
            Add(release, StreamWrapperPhase.OwnedCursorEntry, StreamWrapperPhase.UseChoice, StreamWrapperPhase.MoveUpstream, StreamWrapperPhase.DetachCursor,
                StreamWrapperPhase.DisposeCursor, StreamWrapperPhase.ReleaseResult, StreamWrapperPhase.ReleaseSuccess, StreamWrapperPhase.ReleaseFailure);
            required.Owners.Add(new(who, StreamMultiplicity.EachAcquiredCursor, who == StreamCursorConsumer.Wrapper ? "wrapper-call-or-enumeration" : "consumer-enumeration", true, true, true, false));
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, input + "/envelope/request", input + "/cursor/request", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, input + "/envelope/api-token", input + "/cursor/api-token", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AcquireCursor, who == StreamCursorConsumer.Wrapper ? "Wrapper/enumerator-token-argument" : "Consumer/enumerator-token-argument",
                input + "/cursor/enumerator-token", StreamWrapperOutcome.Success);
            Transfer(acquire, StreamWrapperPhase.AssignOwner, "acquired/upstream-cursor", who + "/owned-cursor");
            required.Calls.Add(new(Site(acquire, StreamWrapperPhase.AcquireCursor), null, input, StreamProgram.GetEnumerator, StreamMultiplicity.EachAcquiredCursor));
            required.Calls.Add(new(Site(release, StreamWrapperPhase.MoveUpstream), null, input, StreamProgram.Move, StreamMultiplicity.ZeroOrMore));
            required.Calls.Add(new(Site(release, StreamWrapperPhase.DisposeCursor), null, input, StreamProgram.Dispose, StreamMultiplicity.EachAcquiredCursor));
            Transfer(release, StreamWrapperPhase.DetachCursor, who + "/owned-cursor", who + "/dispose-cursor");
            Transfer(release, StreamWrapperPhase.DisposeCursor, "cleanup/error", who + "/escaping-error", StreamWrapperOutcome.Failure);
            Transfer(release, StreamWrapperPhase.UseChoice, "opaque-owner/recovery", who + "/escaping-error", StreamWrapperOutcome.Recover);
            required.Awaits.Add(new(Site(release, StreamWrapperPhase.MoveUpstream), "bool", true, true, true, true, true, false));
            required.Awaits.Add(new(Site(release, StreamWrapperPhase.DisposeCursor), "void", true, true, true, true, true, true));
        }
        // C01 direct-return delegation is framework acquisition, not an explicit user token.
        var delegated = StreamWrapperProgram.AcquireDelegatedOutput;
        Add(delegated, StreamWrapperPhase.AcquireCursor, StreamWrapperPhase.AssignOwner, StreamWrapperPhase.CursorReturn, StreamWrapperPhase.AcquireFailure);
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, input + "/envelope/request", input + "/cursor/request", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, input + "/envelope/api-token", input + "/cursor/api-token", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AcquireCursor, output + "/source/token", input + "/cursor/enumerator-token", StreamWrapperOutcome.Success);
        Transfer(delegated, StreamWrapperPhase.AssignOwner, "acquired/upstream-cursor", "Consumer/owned-cursor");
        required.Calls.Add(new(Site(delegated, StreamWrapperPhase.AcquireCursor), null, input, StreamProgram.GetEnumerator, StreamMultiplicity.EachAcquiredCursor));
        return required;
        void Add(StreamWrapperProgram program, params StreamWrapperPhase[] phases)
        {
            required.Entries.Add(program, Site(program, phases[0]));
            required.Sites.AddRange(phases.Select(p => Site(program, p)));
        }
        void Transfer(StreamWrapperProgram program, StreamWrapperPhase phase, string from, string to, StreamWrapperOutcome outcome = StreamWrapperOutcome.Always) =>
            required.Transfers.Add(new(from, to, Site(program, phase), outcome));
    }
}
internal sealed class RequiredStreamWrapperBoundary
{
    internal int Position { get; }
    internal string Input { get; }
    internal string Output { get; }
    internal readonly Dictionary<StreamWrapperProgram, StreamWrapperSite> Entries = new();
    internal readonly List<StreamWrapperSite> Sites = new();
    internal readonly List<StreamWrapperTransfer> Transfers = new();
    internal readonly List<StreamWrapperCursorOwner> Owners = new();
    internal readonly List<StreamWrapperAwait> Awaits = new();
    internal readonly List<StreamWrapperCall> Calls = new();
    internal RequiredStreamWrapperBoundary(int position, string input, string output) { Position = position; Input = input; Output = output; }
}
