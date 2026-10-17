using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal sealed record StreamWrapperTokenTrace(int Position, string OuterRequest, string OuterToken,
    string SelectedRequest, string SelectedToken, string InputRequest, string InputApiToken,
    string InputEnumeratorToken, string InputStartToken);
internal sealed record StreamWrapperTokenFlowResult(EquatableArray<CanonicalExecutionGraphFact> Facts, EquatableArray<StreamWrapperTokenTrace> Traces);

/// <summary>
/// Follows actual operands across a direct-return path, independently of the Required matrices.
/// Distinct cancelable symbolic tokens expose accidental outer-token recombination. This is IR evidence,
/// not a claim about executing generated runtime code or proving opaque Wrapper bodies.
/// </summary>
internal static class StreamWrapperTokenFlowValidator
{
    internal static StreamWrapperTokenFlowResult Trace(BoundPipelineModel route, CanonicalStreamWrapperSchedule candidate,
        bool explicitOuterEnumeration = false, bool explicitInnerEnumeration = false, StreamCursorConsumer explicitConsumer = StreamCursorConsumer.Wrapper)
    {
        var facts = new HashSet<CanonicalExecutionGraphFact>(); var traces = new List<StreamWrapperTokenTrace>();
        var global = new Dictionary<string, string> { ["CancellationToken.None"] = "" };
        var visited = new HashSet<string>();
        void Reject(string reason) => facts.Add(new(reason, route.Anchor));
        Visit(candidate.ConsumerSegment, "request:outer", "token:outer", explicitOuterEnumeration ? "token:explicit-outer" : "",
            "request:outer", "token:outer", explicitOuterEnumeration ? "token:explicit-outer" : "");
        return new(new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal)), new(traces));

        string Visit(string id, string request, string api, string enumeration, string expectedRequest, string expectedApi, string expectedEnumeration)
        {
            if (!visited.Add(id)) { Reject("TokenFlowSegmentCycle"); return "?"; }
            var found = candidate.Segments.Where(s => s.Descriptor.Id == id).ToArray();
            if (found.Length != 1) { Reject("TokenFlowSegmentMissing"); return "?"; }
            var segment = found[0]; var cursor = segment.Cursor;
            var local = new Dictionary<string, string>
            {
                ["api/request"] = request,
                ["api/token"] = api,
                ["enumerator/token"] = enumeration,
                ["CancellationToken.None"] = ""
            };
            ApplyCore(StreamProgram.Create, StreamPhase.CreateEnvelope, StreamOutcome.Always);
            ApplyCore(StreamProgram.GetEnumerator, StreamPhase.CreateCursor, StreamOutcome.Always);
            var actualApi = Read(local, "cursor/api-token"); var actualEnumeration = Read(local, "cursor/enumerator-token");
            local["token-selection/result"] = ActualMerge(cursor, actualApi, actualEnumeration);
            ApplyCore(StreamProgram.Move, StreamPhase.MergeTokens, StreamOutcome.Success);
            var start = Read(local, "cursor/start-token"); var inputRequest = Read(local, "cursor/request");
            var expectedStart = ExpectedMerge(expectedApi, expectedEnumeration);
            if (inputRequest != expectedRequest || actualApi != expectedApi || actualEnumeration != expectedEnumeration || start != expectedStart)
                Reject(explicitInnerEnumeration ? "ExplicitEnumerationTokenWasDiscardedOrOverridden" : "ImplicitWrapperTokenRecombinedWithOpenSelection");
            foreach (var value in local) global[id + "/" + value.Key] = value.Value;
            var factory = segment.Descriptor.Contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory;
            ApplyCore(StreamProgram.Move, factory, StreamOutcome.Success);
            ApplyCore(StreamProgram.Move, StreamPhase.AcquireSource, StreamOutcome.Success);
            global[id + "/source/token"] = Read(local, "source/token");
            if (segment.Descriptor.Contract.SourceKind == StreamSourceKind.Wrapper)
            {
                var protocols = candidate.Wrappers.Where(w => w.OutputSegment == id).ToArray();
                if (protocols.Length != 1) { Reject("TokenFlowWrapperMissing"); return start; }
                var w = protocols[0];
                var chosenRequest = "request:open:" + w.Position; var chosenToken = "token:open:" + w.Position;
                global["Open/request-argument"] = chosenRequest; global["Open/token-argument"] = chosenToken;
                global["Open/returned-enumerable"] = "envelope:" + w.InputSegment;
                global["Wrapper/enumerator-token-argument"] = "token:explicit-inner";
                global["Consumer/enumerator-token-argument"] = "token:explicit-inner";
                ApplyWrapper(StreamWrapperProgram.Invoke, StreamWrapperPhase.Begin, StreamWrapperOutcome.Always);
                if (Read(global, "Wrap/request") != expectedRequest || Read(global, "Wrap/token") != expectedStart ||
                    Read(local, "wrapper/request") != expectedRequest || Read(local, "wrapper/token") != expectedStart) Reject("WrapperStartArgumentsMismatch");
                ApplyWrapper(StreamWrapperProgram.Open, StreamWrapperPhase.SaveRequest, StreamWrapperOutcome.Always);
                ApplyWrapper(StreamWrapperProgram.Open, StreamWrapperPhase.SaveToken, StreamWrapperOutcome.Always);
                ApplyWrapper(StreamWrapperProgram.Invoke, StreamWrapperPhase.DirectReturn, StreamWrapperOutcome.Always);
                if (Read(global, id + "/source-enumerable") != "envelope:" + w.InputSegment) Reject("DirectReturnDoesNotDelegateSelectedEnvelope");
                var acquire = !explicitInnerEnumeration ? StreamWrapperProgram.AcquireDelegatedOutput : explicitConsumer == StreamCursorConsumer.Wrapper
                    ? StreamWrapperProgram.AcquireByWrapper : StreamWrapperProgram.AcquireByConsumer;
                ApplyWrapper(acquire, StreamWrapperPhase.AcquireCursor, StreamWrapperOutcome.Success);
                var childRequest = Read(global, w.InputSegment + "/cursor/request");
                var childApi = Read(global, w.InputSegment + "/cursor/api-token");
                var childEnumeration = Read(global, w.InputSegment + "/cursor/enumerator-token");
                var childStart = Visit(w.InputSegment, childRequest, childApi, childEnumeration, chosenRequest, chosenToken,
                    explicitInnerEnumeration ? "token:explicit-inner" : "");
                traces.Add(new(w.Position, inputRequest, start, chosenRequest, chosenToken, childRequest, childApi, childEnumeration, childStart));
                if (!explicitInnerEnumeration && childStart != chosenToken) Reject("ImplicitWrapperTokenRecombinedWithOpenSelection");
                void ApplyWrapper(StreamWrapperProgram program, StreamWrapperPhase phase, StreamWrapperOutcome outcome)
                {
                    foreach (var t in w.Transfers.Where(t => t.At.Program == program && t.At.Phase == phase && t.Outcome == outcome))
                        global[t.Destination] = Read(global, t.Source);
                }
            }
            else if (Read(local, "handler/request") != expectedRequest || Read(local, "handler/token") != expectedStart || Read(local, "source/token") != expectedStart)
                Reject("HandlerTokenFlowMismatch");
            // Global protocol operands and cursor-local slots denote the same storage.
            // An extra protocol write must be visible to the outer Hook check as well.
            local["cursor/request"] = Read(global, id + "/cursor/request");
            local["cursor/start-token"] = Read(global, id + "/cursor/start-token");
            for (var i = 0; i < segment.Descriptor.Contract.Hooks.Count; i++)
            {
                var phase = segment.Descriptor.Contract.Hooks[i].Callbacks[0].Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map;
                foreach (var t in cursor.Transfers.Where(t => t.At.Program == StreamProgram.Move && t.At.Phase == phase && t.At.Stage == i && t.Outcome == StreamOutcome.Always))
                    local[t.Destination] = Read(local, t.Source);
                if (Read(local, "hook:" + i + "/request") != expectedRequest || Read(local, "hook:" + i + "/token") != expectedStart)
                    Reject("OuterHookRequestOrTokenOverwritten");
            }
            return start;
            void ApplyCore(StreamProgram program, StreamPhase phase, StreamOutcome outcome)
            {
                foreach (var t in cursor.Transfers.Where(t => t.At.Program == program && t.At.Phase == phase && t.Outcome == outcome)) local[t.Destination] = Read(local, t.Source);
            }
        }
        string Read(Dictionary<string, string> values, string key)
        {
            if (values.TryGetValue(key, out var value)) return value;
            // Item results are opaque; only request/token operands participate in this check.
            if (key.Contains("/item") || key == "handler/enumerable" || key == "wrapper/enumerable") return "opaque:" + key;
            Reject("MissingTokenFlowValue:" + key); return "?" + key;
        }
        string ActualMerge(CanonicalStreamExecutionSchedule cursor, string api, string enumeration)
        {
            var @case = api == "" ? StreamTokenCase.ApiNotCancelable : enumeration == "" ? StreamTokenCase.EnumeratorNotCancelable :
                api == enumeration ? StreamTokenCase.SameToken : StreamTokenCase.DistinctCancelable;
            var rules = cursor.Tokens.Where(t => t.Case == @case).ToArray();
            if (rules.Length != 1) { Reject("MissingTokenFlowMergeRule"); return "?"; }
            return rules[0].Source switch
            {
                StreamTokenSource.Api => api,
                StreamTokenSource.Enumerator => enumeration,
                StreamTokenSource.Linked => "link(" + api + ";" + enumeration + ")",
                _ => "?"
            };
        }
    }
    // Derive token-flow expectations independently of the builder and RequiredStream tables.
    private static string ExpectedMerge(string api, string enumeration) => api == "" ? enumeration : enumeration == "" || api == enumeration ? api : "link(" + api + ";" + enumeration + ")";
}
