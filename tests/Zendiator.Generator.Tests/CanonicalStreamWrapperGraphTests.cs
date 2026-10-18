using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
using static Zendiator.SourceGenerator.StreamWrapperNames;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string WrapperGraphFixture(string pipeline) => StreamGraphFixture(pipeline) + """
        public sealed class SecondStreamOpaque : IStreamWrapper<Items,int> {
            public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c)
                where N : IStreamUpstream<Items,int> => n.Open(r,c);
        }
        """;
    private static CanonicalStreamWrapperSchedule ChangeWrapper(CanonicalStreamWrapperSchedule schedule, Func<StreamWrapperProtocol, StreamWrapperProtocol> edit) =>
        schedule with { Wrappers = new(schedule.Wrappers.Select(edit)) };
    private static CanonicalStreamWrapperSchedule ChangeWrapperPath(CanonicalStreamWrapperSchedule schedule, StreamWrapperProgram program,
        StreamWrapperPhase from, StreamWrapperOutcome outcome, StreamWrapperPhase to) => ChangeWrapper(schedule, w => w with
        {
            Flows = new(w.Flows.Select(f => f.Program == program ? f with
            {
                Edges = new(f.Edges.Select(e => e.From == StreamWrapperNames.Site(program, from) && e.Outcome == outcome
                ? e with { To = StreamWrapperNames.Site(program, to) } : e))
            } : f))
        });

    [Fact]
    public void Repeated_stream_wrapper_type_keeps_existing_duplicate_stage_diagnostic()
    {
        BindLists(WrapperGraphFixture("Stage.Wrapper<StreamOpaque>(),Stage.Wrapper<StreamOpaque>()"), "ZEN0035");
    }

    [Theory]
    [InlineData("Stage.Wrapper<StreamOpaque>()")]
    [InlineData("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<DoubleItem>(),Stage.Hook<Small>()")]
    [InlineData("Stage.Wrapper<StreamOpaque>(),Stage.Hook<DoubleItem>(),Stage.Wrapper<SecondStreamOpaque>(),Stage.Hook<Small>()")]
    [InlineData("Stage.Wrapper<StreamOpaque>(),Stage.Wrapper<SecondStreamOpaque>()")]
    public void Stream_wrapper_real_frontend_preserves_source_to_consumer_segments_and_public_acceptance(string pipeline)
    {
        var accepted = pipeline == "Stage.Wrapper<StreamOpaque>()";
        var driver = BindLists(WrapperGraphFixture(pipeline), accepted ? [] : ["ZEN0037"]); var graph = Graph(driver);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Null(graph.StreamSchedule); Assert.Null(graph.Schedule);
        var s = Assert.IsType<CanonicalStreamWrapperSchedule>(graph.StreamWrapperSchedule);
        var positions = graph.Contract.Stages.Select((stage, i) => (stage, i)).Where(x => x.stage.Kind == FusionStageKind.Wrapper).Select(x => x.i).ToArray();
        Assert.Equal(positions.Length + 1, s.Segments.Count); Assert.Equal(positions.Length, s.Wrappers.Count);
        Assert.Equal(StreamSourceKind.Handler, s.Segments[0].Descriptor.Contract.SourceKind);
        Assert.Equal(Segment(positions.Last()), s.ConsumerSegment);
        for (var n = 0; n < s.Segments.Count; n++)
        {
            var segment = s.Segments[n];
            Assert.Empty(CanonicalStreamGraphValidator.ValidateSegment(segment.Descriptor.Contract, segment.Cursor));
            Assert.All(segment.Descriptor.Contract.Hooks, h => Assert.Equal(FusionStageKind.Hook, h.Kind));
            Assert.All(segment.Cursor.Owners.Where(o => o.Borrowed), o => Assert.Equal(StreamCleanup.None, o.Cleanup));
            if (n != 0)
            {
                Assert.Equal(StreamSourceKind.Wrapper, segment.Descriptor.Contract.SourceKind);
                Assert.Equal(s.Segments[n - 1].Descriptor.Id, segment.Descriptor.UpstreamSegment);
                var producer = Assert.Single(segment.Cursor.Sites, site => site.Id.Phase == StreamPhase.WrapperFactory);
                Assert.Equal(graph.Contract.Stages[positions[n - 1]].Callbacks[0].Implementation, producer.Producer);
                Assert.DoesNotContain(segment.Cursor.Sites, site => site.Id.Phase is StreamPhase.SourceFactory or StreamPhase.ResolveHandler);
            }
        }
        Assert.All(s.Wrappers, w =>
        {
            Assert.Equal(StreamMultiplicity.ZeroOrMore, w.OpenCalls); Assert.Equal(StreamMultiplicity.ZeroOrMore, w.EnumerationsPerOpen);
            Assert.True(w.BodyOpaque); Assert.False(w.BodyCleanupProven); Assert.False(w.InsertAsyncAdapter);
            Assert.Equal("wrapper-call-or-enumeration", w.OpenAvailability);
            Assert.All(w.Awaits, a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); Assert.True(a.CompleteBeforeOwnerExit); });
        });
        if (!accepted) Assert.Contains("runtime generation and equivalence are pending", Assert.Single(driver.GetRunResult().Diagnostics).GetMessage());
    }

    [Fact]
    public void Wrapper_selected_open_values_and_enumeration_arguments_do_not_overwrite_outer_state_or_eagerly_start_upstream()
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037"));
        var s = graph.StreamWrapperSchedule!; var w = Assert.Single(s.Wrappers);
        Assert.Equal(Segment(-1), w.InputSegment); Assert.Equal(Segment(1), w.OutputSegment);
        Assert.Equal(new EquatableArray<int>(new[] { 0 }), s.Segments[0].Descriptor.HookPositions);
        Assert.Equal(new EquatableArray<int>(new[] { 2 }), s.Segments[1].Descriptor.HookPositions);
        Assert.Contains(w.Transfers, t => t.Source == "Open/request-argument" && t.Destination == w.InputSegment + "/envelope/request");
        Assert.Contains(w.Transfers, t => t.Source == "Open/token-argument" && t.Destination == w.InputSegment + "/envelope/api-token");
        Assert.Contains(w.Transfers, t => t.Source == "Wrapper/enumerator-token-argument" && t.Destination == w.InputSegment + "/cursor/enumerator-token");
        Assert.Contains(w.Transfers, t => t.Source == "Consumer/enumerator-token-argument" && t.Destination == w.InputSegment + "/cursor/enumerator-token");
        Assert.DoesNotContain(w.Transfers, t => t.Destination == w.OutputSegment + "/cursor/request" || t.Destination == w.OutputSegment + "/cursor/start-token");
        Assert.All(w.Calls.Where(c => c.At.Program is StreamWrapperProgram.Invoke or StreamWrapperProgram.Open), c => Assert.Null(c.CursorTarget));
        Assert.All(w.Calls.Where(c => c.At.Phase == StreamWrapperPhase.AcquireCursor), c => Assert.Equal(StreamProgram.GetEnumerator, c.CursorTarget));
        var selected = Assert.Single(w.Transfers, t => t.Source == "Open/token-argument");
        Reject(ChangeWrapper(s, p => p with { Transfers = new(p.Transfers.Select(t => t == selected ? t with { Source = w.OutputSegment + "/cursor/start-token" } : t)) }), "MissingWrapperTransfer");
        var acquire = Assert.Single(w.Transfers, t => t.Source == "acquired/upstream-cursor" && t.At.Program == StreamWrapperProgram.AcquireByConsumer);
        Reject(ChangeWrapper(s, p => p with { Transfers = new(p.Transfers.Select(t => t == acquire ? t with { Destination = "Wrapper/owned-cursor" } : t)) }), "MissingWrapperTransfer");
        Reject(ChangeWrapper(s, p => p with { Calls = new(p.Calls.Select(c => c.At.Phase == StreamWrapperPhase.CallOpen ? c with { ProtocolTarget = null, SegmentTarget = w.InputSegment, CursorTarget = StreamProgram.Move } : c)) }), "MissingWrapperCall");
        void Reject(CanonicalStreamWrapperSchedule candidate, string reason) => Assert.Contains(CanonicalStreamWrapperValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Theory]
    [InlineData((int)StreamWrapperProgram.Invoke, (int)StreamWrapperPhase.OpenChoice, (int)StreamWrapperOutcome.Zero, (int)StreamWrapperPhase.DirectReturn, "InvalidWrapperExit:DirectReturn")]
    [InlineData((int)StreamWrapperProgram.Invoke, (int)StreamWrapperPhase.OpenChoice, (int)StreamWrapperOutcome.Zero, (int)StreamWrapperPhase.CallOpen, "OpenCallWithoutOpaqueChoice")]
    [InlineData((int)StreamWrapperProgram.Invoke, (int)StreamWrapperPhase.CallOpen, (int)StreamWrapperOutcome.Success, (int)StreamWrapperPhase.OutputChoice, "WrapperOutputBeforeOpaqueCallsComplete")]
    [InlineData((int)StreamWrapperProgram.Open, (int)StreamWrapperPhase.NullGuard, (int)StreamWrapperOutcome.Success, (int)StreamWrapperPhase.SaveToken, "OpenTokenSavedBeforeSelectedRequestOrRepeated")]
    [InlineData((int)StreamWrapperProgram.Open, (int)StreamWrapperPhase.NullGuard, (int)StreamWrapperOutcome.Failure, (int)StreamWrapperPhase.SaveRequest, "OpenRequestSavedBeforeGuardOrRepeated")]
    [InlineData((int)StreamWrapperProgram.Open, (int)StreamWrapperPhase.SaveRequest, (int)StreamWrapperOutcome.Always, (int)StreamWrapperPhase.EnumerableReturn, "InvalidWrapperExit:EnumerableReturn")]
    [InlineData((int)StreamWrapperProgram.AcquireByWrapper, (int)StreamWrapperPhase.AcquireCursor, (int)StreamWrapperOutcome.Failure, (int)StreamWrapperPhase.AssignOwner, "CursorOwnerBeforeSuccessfulAcquisition")]
    [InlineData((int)StreamWrapperProgram.AcquireByConsumer, (int)StreamWrapperPhase.AcquireCursor, (int)StreamWrapperOutcome.Success, (int)StreamWrapperPhase.CursorReturn, "InvalidWrapperExit:CursorReturn")]
    [InlineData((int)StreamWrapperProgram.ReleaseByWrapper, (int)StreamWrapperPhase.OwnedCursorEntry, (int)StreamWrapperOutcome.Always, (int)StreamWrapperPhase.DisposeCursor, "CursorCleanupBeforeDetachOrRepeated")]
    [InlineData((int)StreamWrapperProgram.ReleaseByConsumer, (int)StreamWrapperPhase.UseChoice, (int)StreamWrapperOutcome.Finish, (int)StreamWrapperPhase.ReleaseSuccess, "InvalidWrapperExit:ReleaseSuccess")]
    [InlineData((int)StreamWrapperProgram.ReleaseByWrapper, (int)StreamWrapperPhase.DisposeCursor, (int)StreamWrapperOutcome.Failure, (int)StreamWrapperPhase.ReleaseSuccess, "InvalidWrapperExit:ReleaseSuccess")]
    [InlineData((int)StreamWrapperProgram.ReleaseByConsumer, (int)StreamWrapperPhase.DetachCursor, (int)StreamWrapperOutcome.Always, (int)StreamWrapperPhase.MoveUpstream, "UpstreamMoveAfterDetachOrOutsideOwnedLifetime")]
    [InlineData((int)StreamWrapperProgram.ReleaseByWrapper, (int)StreamWrapperPhase.DisposeCursor, (int)StreamWrapperOutcome.Success, (int)StreamWrapperPhase.DisposeCursor, "CursorCleanupBeforeDetachOrRepeated")]
    [InlineData((int)StreamWrapperProgram.ReleaseByWrapper, (int)StreamWrapperPhase.ReleaseResult, (int)StreamWrapperOutcome.Failure, (int)StreamWrapperPhase.UseChoice, "CursorUsedOutsideItsOwnerLifetime")]
    [InlineData((int)StreamWrapperProgram.ReleaseByConsumer, (int)StreamWrapperPhase.UseChoice, (int)StreamWrapperOutcome.Continue, (int)StreamWrapperPhase.UseChoice, "CursorUsedOutsideItsOwnerLifetime")]
    public void Wrapper_actual_paths_reject_short_circuit_value_capture_acquisition_and_cleanup_errors_with_metadata_unchanged(int program, int from, int outcome, int to, string reason)
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037")); var s = graph.StreamWrapperSchedule!;
        var candidate = ChangeWrapperPath(s, (StreamWrapperProgram)program, (StreamWrapperPhase)from, (StreamWrapperOutcome)outcome, (StreamWrapperPhase)to);
        Assert.Equal(s.Wrappers[0].Transfers, candidate.Wrappers[0].Transfers); Assert.Equal(s.Wrappers[0].Owners, candidate.Wrappers[0].Owners);
        Assert.Equal(s.Wrappers[0].Calls, candidate.Wrappers[0].Calls); Assert.Equal(s.Wrappers[0].Awaits, candidate.Wrappers[0].Awaits);
        Assert.Contains(CanonicalStreamWrapperValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Fact]
    public void Wrapper_independent_requirements_reject_scope_aliases_multiplicity_ownership_and_await_or_body_claims()
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037")); var s = graph.StreamWrapperSchedule!;
        Reject(s with { ConsumerSegment = s.Segments[0].Descriptor.Id }, "ConsumerSegmentMismatch");
        Reject(s with { Segments = new(s.Segments.Select(segment => segment.Descriptor.SourcePosition == -1 ? segment with { Descriptor = segment.Descriptor with { HookPositions = new(new[] { 0, 2 }) } } : segment)) }, "MissingSegment");
        Reject(ChangeWrapper(s, w => w with { InputSegment = w.OutputSegment }), "WrapperBoundaryMismatch");
        Reject(ChangeWrapper(s, w => w with { OpenCalls = StreamMultiplicity.Once }), "IndependentLazyOpenMultiplicityRequired");
        Reject(ChangeWrapper(s, w => w with { FreshCursorEachAcquisition = false }), "IndependentLazyOpenMultiplicityRequired");
        Reject(ChangeWrapper(s, w => w with { BodyCleanupProven = true }), "OpaqueWrapperBodyBoundaryMismatch");
        Reject(ChangeWrapper(s, w => w with { InsertAsyncAdapter = true }), "OpaqueWrapperBodyBoundaryMismatch");
        Reject(ChangeWrapper(s, w => w with { OpenAvailability = "factory-only" }), "OpenRestrictedToSynchronousFactory");
        Reject(ChangeWrapper(s, w => w with { Owners = new(w.Owners.Select(o => o with { Acquisitions = StreamMultiplicity.Once, Lifetime = "after-wrapper-exit" })) }), "MissingWrapperCursorOwner");
        Reject(ChangeWrapper(s, w => w with { Awaits = new(w.Awaits.Select(a => a with { ConsumeOnce = false, PreserveIdentityAndToken = false })) }), "WrapperAwaitConsumptionOrFailureMismatch");
        Reject(ChangeWrapper(s, w => w with { Awaits = new(w.Awaits.Append(w.Awaits[0])) }), "UnexpectedWrapperAwait");
        Reject(ChangeWrapper(s, w => w with { Awaits = new(w.Awaits.Select(a => a.Producer.Phase == StreamWrapperPhase.DisposeCursor ? a with { LastErrorReplaces = false } : a)) }), "MissingWrapperAwait");
        var outer = s.Segments[1];
        var corrupted = outer.Cursor with
        {
            Flows = new(outer.Cursor.Flows.Select(f => f.Program == StreamProgram.Move ? f with
            {
                Edges = new(f.Edges.Select(e =>
            e.From.Phase == StreamPhase.PreCancel && e.Outcome == StreamOutcome.Success ? e with { To = StreamExecutionNames.Id(StreamProgram.Move, StreamPhase.WrapperFactory) } : e))
            } : f))
        };
        Reject(s with { Segments = new(s.Segments.Select(segment => segment == outer ? segment with { Cursor = corrupted } : segment)) }, "SourceFactoryBeforeResolveOrRepeated");
        void Reject(CanonicalStreamWrapperSchedule candidate, string reason) => Assert.Contains(CanonicalStreamWrapperValidator.Validate(graph.Contract, candidate), f => f.Reason == reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Wrapper_body_zero_many_or_native_async_implementation_is_opaque_to_incremental_graph_and_locations_stay_fresh(int body)
    {
        var source = WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()");
        var driver = BindLists(source, "ZEN0037"); var previous = Graph(driver);
        var changed = body switch
        {
            0 => source.Replace("=> n.Open(r,c);", "=> Empty(); private static async System.Collections.Generic.IAsyncEnumerable<int> Empty() { await Task.Yield(); yield break; }"),
            1 => source.Replace("=> n.Open(r,c);", "{ n.Open(r,c); n.Open(r,c); return n.Open(r,c); }"),
            _ => source.Replace("public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>", "public async System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>")
                .Replace("=> n.Open(r,c);", "{ await foreach(var item in n.Open(r,c)) yield return item; }")
        };
        var compilation = Compilation("\n\n" + changed); driver = driver.RunGenerators(compilation);
        GraphReason(driver, IncrementalStepRunReason.Cached); Assert.Equal(previous, Graph(driver));
        Assert.Same(compilation.SyntaxTrees.Single(), Assert.Single(driver.GetRunResult().Diagnostics).Location.SourceTree);
        var reordered = source.Replace("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()", "Stage.Wrapper<StreamOpaque>(),Stage.Hook<Positive>(),Stage.Hook<Small>()");
        driver = driver.RunGenerators(Compilation(reordered)); GraphReason(driver, IncrementalStepRunReason.Modified);
        Assert.True(Graph(driver).IsValid); Assert.NotEqual(previous.StreamWrapperSchedule, Graph(driver).StreamWrapperSchedule);
    }

    [Fact]
    public void Wrapper_owned_service_and_unknown_completion_still_block_canonical_graph_creation()
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Wrapper<StreamOpaque>()")));
        var wrapper = graph.Contract.Stages[0];
        var owned = CanonicalExecutionGraphNormalizer.Create(graph.Contract with { Stages = new(new[] { wrapper with { ServiceOwnership = FusionOwnership.OwnedSession } }) });
        Assert.False(owned.IsValid); Assert.Null(owned.StreamWrapperSchedule);
        Assert.Contains(owned.Facts, f => f.Reason == "OwnedStageAcquisitionAndCleanupUnavailable");
        foreach (var completion in new[] { BoundCompletionKind.ValueTask, BoundCompletionKind.Synchronous, (BoundCompletionKind)(-1), (BoundCompletionKind)42 })
        {
            var bad = CanonicalExecutionGraphNormalizer.Create(graph.Contract with { Stages = new(new[] { wrapper with { Callbacks = new(new[] { wrapper.Callbacks[0] with { Completion = completion } }) } }) });
            Assert.False(bad.IsValid); Assert.Null(bad.StreamWrapperSchedule); Assert.Contains(bad.Facts, f => f.Reason == "StreamWrapperCompletionMismatch");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_return_whole_path_preserves_selected_open_token_and_outer_hook_values_even_with_explicit_outer_enumeration(bool explicitOuter)
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037"));
        var result = StreamWrapperTokenFlowValidator.Trace(graph.Contract, graph.StreamWrapperSchedule!, explicitOuterEnumeration: explicitOuter);
        Assert.Empty(result.Facts);
        var trace = Assert.Single(result.Traces);
        Assert.Equal("request:outer", trace.OuterRequest);
        Assert.Equal(explicitOuter ? "link(token:outer;token:explicit-outer)" : "token:outer", trace.OuterToken);
        Assert.Equal("request:open:1", trace.InputRequest);
        Assert.Equal("token:open:1", trace.InputApiToken);
        Assert.Equal("", trace.InputEnumeratorToken); // Framework output acquisition supplies no implicit outer token.
        Assert.Equal(trace.SelectedToken, trace.InputStartToken);
        Assert.NotEqual(trace.OuterToken, trace.InputStartToken);
    }

    [Theory]
    [InlineData((int)StreamCursorConsumer.Wrapper)]
    [InlineData((int)StreamCursorConsumer.Consumer)]
    public void Explicit_upstream_enumerator_token_still_combines_with_selected_open_token_under_D02(int consumer)
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037"));
        var result = StreamWrapperTokenFlowValidator.Trace(graph.Contract, graph.StreamWrapperSchedule!, explicitInnerEnumeration: true, explicitConsumer: (StreamCursorConsumer)consumer);
        Assert.Empty(result.Facts); var trace = Assert.Single(result.Traces);
        Assert.Equal("token:open:1", trace.InputApiToken);
        Assert.Equal("token:explicit-inner", trace.InputEnumeratorToken);
        Assert.Equal("link(token:open:1;token:explicit-inner)", trace.InputStartToken);
        Assert.Equal("request:outer", trace.OuterRequest); Assert.Equal("token:outer", trace.OuterToken);
    }

    [Fact]
    public void Original_implicit_outer_token_bug_is_rejected_by_cross_path_oracle_without_using_matching_required_tables()
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037")); var s = graph.StreamWrapperSchedule!;
        var oldBug = s with
        {
            Segments = new(s.Segments.Select(segment => segment.Descriptor.Contract.SourceKind == StreamSourceKind.Wrapper ? segment with
            {
                Cursor = segment.Cursor with
                {
                    Transfers = new(segment.Cursor.Transfers.Select(t => t.At.Phase == StreamPhase.AcquireSource && t.Destination == "source/token"
                    ? t with { Source = "cursor/start-token" } : t))
                }
            } : segment))
        };
        // Keep token-flow expectations independent of the Required matrices.
        var result = StreamWrapperTokenFlowValidator.Trace(graph.Contract, oldBug);
        Assert.Contains(result.Facts, f => f.Reason == "ImplicitWrapperTokenRecombinedWithOpenSelection");
        Assert.Equal("link(token:open:1;token:outer)", Assert.Single(result.Traces).InputStartToken);
        Assert.Equal(s.Wrappers, oldBug.Wrappers); Assert.Equal(s.Segments[1].Cursor.Flows, oldBug.Segments[1].Cursor.Flows);
    }

    [Fact]
    public void Delegated_acquisition_and_outer_hook_token_writes_are_checked_across_actual_storage_slots()
    {
        var graph = Graph(BindLists(WrapperGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037")); var s = graph.StreamWrapperSchedule!;
        var injected = ChangeWrapper(s, w => w with
        {
            Transfers = new(w.Transfers.Select(t => t.At.Program == StreamWrapperProgram.AcquireDelegatedOutput &&
            t.Destination == w.InputSegment + "/cursor/enumerator-token" ? t with { Source = w.OutputSegment + "/cursor/start-token" } : t))
        });
        Assert.Contains(StreamWrapperTokenFlowValidator.Trace(graph.Contract, injected).Facts, f => f.Reason == "ImplicitWrapperTokenRecombinedWithOpenSelection");
        var overwritten = ChangeWrapper(s, w => w with
        {
            Transfers = new(w.Transfers.Append(new StreamWrapperTransfer("Open/token-argument",
            w.OutputSegment + "/cursor/start-token", StreamWrapperNames.Site(StreamWrapperProgram.Open, StreamWrapperPhase.SaveToken), StreamWrapperOutcome.Always)))
        });
        Assert.Contains(StreamWrapperTokenFlowValidator.Trace(graph.Contract, overwritten).Facts, f => f.Reason == "OuterHookRequestOrTokenOverwritten");
    }
}
