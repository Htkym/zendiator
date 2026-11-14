using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Competitive;

internal static class Perf04InputGate
{
    internal const string Protocol = "zendiator-perf04-input-gate-v1";
    internal const string EvidencePath = "perf04-input/perf04-input.json";
    internal const int ExpectedCases = 14;
    private static readonly List<object> Cases = [];
    private static readonly List<string> CaseNames = [];

    internal static async Task Run(string outputDirectory, string implementationSha256)
    {
        Require(implementationSha256.Length == 64 &&
            implementationSha256.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'),
            "PERF-04 requires an uppercase SHA256 implementation identity.");
        var original = Correctness.Results.ToArray();
        Cases.Clear();
        CaseNames.Clear();
        await GeneratedPerf04InputGate.Run().ConfigureAwait(false);
        Require(Cases.Count == ExpectedCases && CaseNames.Distinct(StringComparer.Ordinal).Count() == ExpectedCases,
            "PERF-04 input case set changed.");
        Require(Correctness.Results.Count == original.Length &&
            Correctness.Results.Zip(original).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "PERF-04 changed the original correctness results.");
        var generatedPooledProof = await CheckGeneratedProducer().ConfigureAwait(false);
        var evidence = new
        {
            schemaVersion = 1,
            protocol = Protocol,
            implementationSha256,
            caseSet = CaseNames.Order(StringComparer.Ordinal).ToArray(),
            cases = Cases,
            generatedPooledProof,
            status = "Passed"
        };
        var path = Path.Combine(outputDirectory, EvidencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ArtifactEvidence.Write(path, evidence);
    }

    internal static async Task Check(string type, string input, string family, int behaviors,
        Action<int, bool> setup, Action cleanup,
        Func<int, bool, Probe, CancellationToken, IAsyncEnumerable<int>> stream,
        Func<int, bool, Probe, Zendiator.IStreamSink<int>, CancellationToken, ValueTask>? typed,
        Func<string, ValueTask<int>> measured)
    {
        var first = Correctness.Results.Count;
        object[] correctness;
        setup(16, false);
        try
        {
            await Correctness.Stream(type, behaviors, stream).ConfigureAwait(false);
            if (family == "Relay5")
            {
                var library = type.StartsWith("Zendiator", StringComparison.Ordinal) ? "Zendiator" : type;
                await Correctness.RelayTiming(library, behaviors, probe => stream(2, false, probe, default)).ConfigureAwait(false);
            }
            if (typed is not null)
                await Correctness.TypedStream(type, behaviors, typed).ConfigureAwait(false);
            correctness = Correctness.Results.Skip(first).ToArray();
            Require(correctness.Length == (family == "Relay5" ? 9 : 8), "PERF-04 semantic helper coverage changed.");
        }
        finally
        {
            // Reuse the existing checks without merging their supplemental rows into the original 389.
            Correctness.Results.RemoveRange(first, Correctness.Results.Count - first);
            cleanup();
        }

        var measuredConsumers = new List<object>();
        string[] methods = typed is null ? ["Full", "EarlyBreak"] : ["Full", "EarlyBreak", "FullTyped", "EarlyBreakTyped"];
        foreach (var method in methods)
            foreach (var asynchronous in new[] { false, true })
            {
                var full = method.StartsWith("Full", StringComparison.Ordinal);
                var count = full ? 1024 : 16;
                setup(count, asynchronous);
                try
                {
                    // These delegates invoke the actual benchmark method, not a replacement consumer.
                    var result = await measured(method).ConfigureAwait(false);
                    Require(result == (full ? 523776 : 0), $"{type}.{method}: measured consumer result.");
                    measuredConsumers.Add(new { method, count, asynchronous, result, status = "Passed" });
                }
                finally
                {
                    cleanup();
                }
            }
        var completedProtocols = new List<string>
        {
            "full-count-order-disposal", "early-break-disposal", "partial-cancellation-disposal",
            "request-precancel", "enumeration-precancel", "producer-exception"
        };
        if (family == "Relay5")
        {
            completedProtocols.Add("relay-order-and-failure");
            if (type.StartsWith("Zendiator", StringComparison.Ordinal))
                completedProtocols.Add("relay-deferred-start");
        }
        if (typed is not null)
            completedProtocols.AddRange(["typed-count-order-disposal", "typed-stop-and-cancel", "typed-exception-disposal"]);
        Cases.Add(new
        {
            type,
            input,
            family,
            behaviors,
            producerRepresentation = type.StartsWith("Zendiator", StringComparison.Ordinal) && input == "GeneratedPooled"
                ? "generated-pooled-named-cursor" : "compiler-async-iterator",
            correctness,
            completedProtocols,
            typedTerminal = typed is not null,
            measuredConsumers,
            status = "Passed"
        });
        CaseNames.Add(type);
    }

    private static async Task<object> CheckGeneratedProducer()
    {
        var type = typeof(Perf04GeneratedWorkStreamCursor);
        var original = type.GetMethod("MovePendingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var generated = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name.StartsWith("__ZendiatorPooled_", StringComparison.Ordinal));
        Require(original.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == "Zendiator.GeneratePooledProducerAttribute"), "Missing generated producer opt-in.");
        Require(original.GetCustomAttribute<AsyncMethodBuilderAttribute>() is null,
            "The generated producer must not be a manually attributed pooled method.");
        Require(BuilderFields(original).Contains(typeof(AsyncValueTaskMethodBuilder<bool>)) &&
            BuilderFields(generated).Contains(typeof(PoolingAsyncValueTaskMethodBuilder<bool>)),
            "Generated producer state-machine builders differ from the promised opt-in.");

        var move = type.GetMethod(nameof(IAsyncEnumerator<int>.MoveNextAsync))!;
        var moveCalls = CalledMethods(move).ToArray();
        var interceptor = moveCalls.Single(method => method.Name == "Intercept" &&
            method.DeclaringType?.FullName?.StartsWith("Zendiator.Generated.Interceptors.PooledProducer_", StringComparison.Ordinal) == true);
        Require(!moveCalls.Contains(original) && CalledMethods(interceptor).Contains(generated) &&
            interceptor.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.Namespace == "System.Runtime.CompilerServices" &&
                attribute.AttributeType.Name.EndsWith("__InterceptsLocationAttribute", StringComparison.Ordinal) &&
                attribute.AttributeType.IsSealed && attribute.AttributeType.BaseType == typeof(Attribute) &&
                attribute.Constructor.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(int), typeof(string) })),
            "The actual producer call site does not reach the generated pooled helper through its interceptor.");

        var probe = new Probe();
        var cursor = new Perf04GeneratedWorkStreamCursor(2, true, probe, default);
        var consumed = 0;
        try
        {
            for (var expected = 0; expected < 2; expected++)
            {
                var context = new QueuedContext();
                var previous = SynchronizationContext.Current;
                ValueTask<bool> pending;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    pending = cursor.MoveNextAsync();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
                Require(!pending.IsCompleted && context.Count == 1, "Generated Task.Yield must be demonstrably pending.");
                var backing = typeof(ValueTask<bool>).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(field => field.FieldType == typeof(object)).GetValue(pending);
                Require(backing is IValueTaskSource<bool> &&
                    backing.GetType().FullName!.Contains("PoolingAsyncValueTaskMethodBuilder", StringComparison.Ordinal),
                    "The intercepted producer did not return a pooled ValueTask source.");
                context.Drain();
                Require(await pending.ConfigureAwait(false) && cursor.Current == expected, "Generated producer sequential result.");
                consumed++;
                // Do not read or await this ValueTask again, including after the next Move reuses a pooled source.
            }
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
        Require(consumed == 2 && probe.Handlers == 1 && probe.Items == 2 && probe.Disposals == 1,
            "Generated producer single-consumption or cleanup failed.");
        return new
        {
            producerType = type.FullName,
            originalMethod = original.Name,
            generatedHelper = generated.Name,
            interceptorType = interceptor.DeclaringType!.FullName,
            interceptorMethod = interceptor.Name,
            callSite = move.Name,
            sourceAttribute = "Zendiator.GeneratePooledProducerAttribute",
            builderType = typeof(PoolingAsyncValueTaskMethodBuilder<bool>).FullName,
            originalBuilderType = typeof(AsyncValueTaskMethodBuilder<bool>).FullName,
            callSiteIntercepted = true,
            helperIsPooled = true,
            manualBuilderAttribute = false,
            forcedPending = true,
            singleConsumption = true,
            sequentialMoves = 2,
            moveResultsConsumed = consumed,
            items = probe.Items,
            disposals = probe.Disposals,
            consumerAssemblySha256 = ArtifactEvidence.Hash(type.Assembly.Location),
            status = "Passed"
        };
    }

    private static Type[] BuilderFields(MethodInfo method) =>
        (method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Producer has no compiled async state machine."))
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Select(field => field.FieldType).ToArray();

    // Decode operand widths before resolving calls so operand bytes cannot masquerade as opcodes.
    private static IEnumerable<MethodInfo> CalledMethods(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("Producer identity requires compiled IL.");
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        for (var offset = 0; offset < bytes.Length;)
        {
            ushort key = bytes[offset++];
            if (key == 0xfe)
                key = (ushort)(0xfe00 | bytes[offset++]);
            var code = codes[key];
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(bytes, offset) * 4,
                _ => 4
            };
            if ((code == OpCodes.Call || code == OpCodes.Callvirt) &&
                method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset)) is MethodInfo target)
                yield return target;
            offset += size;
        }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        internal int Count => callbacks.Count;
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        internal void Drain()
        {
            while (callbacks.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
