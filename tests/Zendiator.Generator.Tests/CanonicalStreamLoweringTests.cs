using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Stream_lowering_changes_for_valid_order_and_rejects_mutated_schedule_data()
    {
        var fixture = StreamPrototype.Value; var row = fixture.RouteGraph;
        var schedule = row.Graph!.StreamSchedule!;
        var original = CanonicalStreamPrototypeProjection.Create(row)!;
        Assert.Equal(new[] { StreamPhase.SourceGuard, StreamPhase.SourceMove, StreamPhase.SourceCurrent,
            StreamPhase.HookGuard, StreamPhase.ResolveHook, StreamPhase.Filter,
            StreamPhase.HookGuard, StreamPhase.ResolveHook, StreamPhase.Map,
            StreamPhase.HookGuard, StreamPhase.ResolveHook, StreamPhase.Filter, StreamPhase.CacheCurrent },
            original.ItemRegion.Steps.Select(s => s.Id.Phase));
        Assert.Equal(new[] { StreamPhase.DetachSource, StreamPhase.SourcePresent, StreamPhase.DisposeSource },
            original.CleanupRegion.Steps.Select(s => s.Id.Phase));

        // Same supported shape, different declaration order: new validated schedule changes code.
        var route = row.Input.Route;
        var swapped = route with { Stages = new(new[] { route.Stages[2], route.Stages[1], route.Stages[0] }) };
        var reordered = row with { Input = row.Input with { Route = swapped }, Graph = CanonicalExecutionGraphNormalizer.Create(swapped) };
        Assert.True(reordered.Graph!.IsValid);
        var changed = Assert.IsType<CanonicalStreamPrototypeModel>(CanonicalStreamPrototypeProjection.Create(reordered));
        Assert.NotEqual(CanonicalStreamPrototypeEmitter.Emit(original).Source, CanonicalStreamPrototypeEmitter.Emit(changed).Source);

        void Reject(CanonicalStreamExecutionSchedule value) => Assert.Null(CanonicalStreamPrototypeProjection.Create(
            row with { Graph = row.Graph! with { StreamSchedule = value } }));
        Reject(schedule with { Sites = new(schedule.Sites.Select(s => s.Id.Phase == StreamPhase.SourceMove
            ? s with { Completion = BoundCompletionKind.Synchronous } : s)) });
        Reject(schedule with { Flows = new(schedule.Flows.Select(f => f.Program == StreamProgram.Move ? f with
        { Edges = new(f.Edges.Select(e => e.From.Phase == StreamPhase.SourceCurrent && e.Outcome == StreamOutcome.Success
            ? e with { To = new(StreamProgram.Move, StreamPhase.CacheCurrent) } : e)) } : f)) });
        Reject(schedule with { Transfers = new(schedule.Transfers.Select(t => t.At.Phase == StreamPhase.SourceCurrent
            ? t with { Destination = "wrong/item" } : t)) });
        Reject(schedule with { Awaits = new(schedule.Awaits.Select(a => a.Producer.Phase == StreamPhase.SourceMove
            ? a with { ConfigureAwaitFalse = false } : a)) });
        Reject(schedule with { Owners = new(schedule.Owners.Select(o => o.Id == "source" ? o with { Borrowed = true } : o)) });
        Reject(schedule with { Flows = new(schedule.Flows.Select(f => f.Program == StreamProgram.Dispose ? f with
        { Edges = new(f.Edges.Select(e => e.From.Phase == StreamPhase.DetachSource
            ? e with { To = new(StreamProgram.Dispose, StreamPhase.SourcePresent) } : e)) } : f)) });
        Reject(schedule with { Flows = new(schedule.Flows.Select(f => f.Program == StreamProgram.Move ? f with
        { Edges = new(f.Edges.Select(e => e.From.Phase == StreamPhase.SourceGuard && e.Outcome == StreamOutcome.Success
            ? e with { To = new(StreamProgram.Move, (StreamPhase)999) } : e)) } : f)) });
    }
}
