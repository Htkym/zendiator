using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Competitive
{
    [ZendiatorComposition("perf03ProtocolStreams")]
    public sealed class Perf03ProtocolStreams;
}

namespace Competitive.Perf03.Diagnostics
{
    internal static class Perf03ProtocolRegistration
    {
        internal static ServiceProvider Create(Perf03ProtocolScenario scenario)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(scenario);
            services.AddZendiator<Perf03ProtocolStreams>(configuration =>
            {
                configuration.Handlers<Stream0>(DispatchFamily.Stream, typeof(Perf03ProtocolStream0));
                configuration.Pipeline<Stream0>(DispatchFamily.Stream);
                configuration.Handlers<Relay5>(DispatchFamily.Stream, typeof(Perf03ProtocolRelay5));
                configuration.Pipeline<Relay5>(DispatchFamily.Stream,
                    Stage.Wrapper<ZRBehavior5<Relay5, int>>(), Stage.Wrapper<ZRBehavior4<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior3<Relay5, int>>(), Stage.Wrapper<ZRBehavior2<Relay5, int>>(),
                    Stage.Wrapper<ZRBehavior1<Relay5, int>>());
            });
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    [Handler]
    public sealed class Perf03ProtocolStream0(Perf03ProtocolScenario scenario, IServiceProvider services)
        : IStreamSourceHandler<Stream0, int, Perf03ProtocolCursor>
    {
        public ValueTask<Perf03ProtocolCursor> OpenAsync(Stream0 request, CancellationToken token)
        {
            scenario.Open(request, token, this, services);
            return new(new Perf03ProtocolCursor(scenario));
        }
    }

    [Handler]
    public sealed class Perf03ProtocolRelay5(Perf03ProtocolScenario scenario, IServiceProvider services)
        : IStreamSourceHandler<Relay5, int, Perf03ProtocolCursor>
    {
        public ValueTask<Perf03ProtocolCursor> OpenAsync(Relay5 request, CancellationToken token)
        {
            scenario.Open(request, token, this, services);
            return new(new Perf03ProtocolCursor(scenario));
        }
    }

    public sealed class Perf03ProtocolScenario : IDisposable
    {
        internal Perf03ProtocolScenario(string name, bool relay)
        {
            Name = name;
            bool eof = name == "pending-eof";
            string caseId = (relay ? "Relay5/" : "Stream0/") + name;
            string phase = name == "first-pending-success" ? "first-move" : eof ? "eof" : "repeated-item";
            Perf03ProtocolTicket Ticket(int generation, string? operationPhase = null) =>
                new(caseId + "/" + (operationPhase ?? phase) + "/" + generation, operationPhase ?? phase, generation);
            MoveGates = name switch
            {
                "synchronous-raw" => [new Perf03AsyncOnceGate(true, Ticket(1, "first-move"), "precompleted-success"), new Perf03AsyncOnceGate(false, Ticket(2, "eof"), "precompleted-success")],
                "completed-fault" => [new Perf03AsyncOnceGate(true, Ticket(1), "precompleted-fault")],
                "completed-cancelled" => [new Perf03AsyncOnceGate(true, Ticket(1), "precompleted-cancelled")],
                "before-registration" => [new Perf03RegistrationGate("before-registration", Ticket(1))],
                "inside-registration" => [new Perf03RegistrationGate("inside-registration", Ticket(1))],
                "consumption-reentry" => [new Perf03RegistrationGate("manual", Ticket(1)), new Perf03RegistrationGate("manual", Ticket(2))],
                "pending-cleanup-success" or "pending-cleanup-failure" => [],
                _ => [new Perf03AsyncOnceGate(!eof, Ticket(1))]
            };
            if (name is "pending-cleanup-success" or "pending-cleanup-failure")
                CleanupGate = new Perf03AsyncOnceGate(true, new(caseId + "/cleanup/1", "cleanup", 1));
            if (name == "synchronous-raw")
                foreach (var gate in MoveGates)
                    gate.Complete();
            if (name == "completed-fault")
            {
                ClassifierFailure = new ArithmeticException("Expected completed PERF-03 protocol fault.");
                MoveGates[0].Fail(ClassifierFailure);
            }
            if (name == "completed-cancelled")
            {
                SourceCancellation = new CancellationTokenSource();
                SourceCancellation.Cancel();
                MoveGates[0].Fail(new OperationCanceledException(SourceCancellation.Token));
            }
        }

        internal string Name
        {
            get;
        }
        internal Probe Probe { get; } = new();
        internal Perf03ProtocolRawGate[] MoveGates
        {
            get;
        }
        internal Perf03AsyncOnceGate? CleanupGate
        {
            get;
        }
        internal Exception? ClassifierFailure
        {
            get;
        }
        internal CancellationTokenSource? SourceCancellation
        {
            get;
        }
        internal object ExpectedRequest { get; set; } = null!;
        internal CancellationToken ExpectedToken
        {
            get; set;
        }
        internal IServiceProvider ExpectedScope { get; set; } = null!;
        internal object? Handler
        {
            get; private set;
        }
        internal int Opens, Pulls, Reads, Disposals;

        internal void Open(object request, CancellationToken token, object handler, IServiceProvider scope)
        {
            Check(ReferenceEquals(request, ExpectedRequest), "Protocol handler received a different request.");
            Check(token == ExpectedToken, "Protocol handler received a different API token.");
            Check(ReferenceEquals(scope, ExpectedScope), "Protocol handler was resolved outside the native scope.");
            Check(++Opens == 1, "Protocol handler opened twice.");
            Handler = handler;
            Probe.Handlers++;
            Probe.Events.Add(100);
        }

        internal static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        public void Dispose() => SourceCancellation?.Dispose();
    }

    public sealed class Perf03ProtocolCursor : IAsyncEnumerator<int>, IValueTaskSource<bool>
    {
        private readonly Perf03ProtocolScenario scenario;
        private bool closed;
        private int nextValue;
        private int current;

        internal Perf03ProtocolCursor(Perf03ProtocolScenario scenario) => this.scenario = scenario;

        public int Current
        {
            get
            {
                scenario.Reads++;
                return current;
            }
        }

        public ValueTask<bool> MoveNextAsync()
        {
            Perf03ProtocolScenario.Check(!closed, "Protocol source pulled after cleanup.");
            int step = scenario.Pulls++;
            if (scenario.Name == "synchronous-raw")
                return step < 2 ? new(this, checked((short)step)) : new(false);
            if (scenario.Name == "first-pending-success")
                return step == 0 ? new(this, 0) : new(false);
            if (step == 0)
                return new(Publish(true));
            int gate = step - 1;
            return gate < scenario.MoveGates.Length ? new(this, checked((short)gate)) : new(false);
        }

        public ValueTask DisposeAsync()
        {
            if (closed)
                return default;
            closed = true;
            scenario.Disposals++;
            scenario.Probe.Disposals++;
            return scenario.CleanupGate?.Void ?? default;
        }

        public bool GetResult(short token) => Publish(Gate(token).GetResult(0));
        public ValueTaskSourceStatus GetStatus(short token) => Gate(token).GetStatus(0);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            Gate(token).OnCompleted(continuation, state, 0, flags);

        private Perf03ProtocolRawGate Gate(short token)
        {
            Perf03ProtocolScenario.Check(token >= 0 && token < scenario.MoveGates.Length, "Unknown protocol source token.");
            return scenario.MoveGates[token];
        }

        private bool Publish(bool moved)
        {
            if (moved)
            {
                current = nextValue++;
                scenario.Probe.Items++;
            }
            return moved;
        }
    }

    internal sealed record Perf03ProtocolTicket(string Id, string Phase, int Generation);

    internal static class Perf03ProtocolAttribution
    {
        internal static readonly AsyncLocal<Perf03ProtocolTicket?> Current = new();

        internal static TicketScope Enter(Perf03ProtocolTicket ticket) => new(ticket);

        internal readonly struct TicketScope : IDisposable
        {
            private readonly Perf03ProtocolTicket? previous;

            internal TicketScope(Perf03ProtocolTicket ticket)
            {
                previous = Current.Value;
                Current.Value = ticket;
            }

            public void Dispose() => Current.Value = previous;
        }
    }

    internal abstract class Perf03ProtocolRawGate(Perf03ProtocolTicket ticket) : IValueTaskSource<bool>, IValueTaskSource
    {
        private readonly TaskCompletionSource<bool> callbackExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<string> returnedStatuses = new();
        internal int StatusChecks, Registrations, Consumptions, Callbacks, InlineCallbacks;
        internal bool Completing
        {
            get; set;
        }
        internal int CompletingThread
        {
            get; set;
        }
        internal Perf03ProtocolTicket Ticket { get; } = ticket;
        internal Task CallbackExited => callbackExited.Task;
        internal string? IssuedParentTicket, RegistrationParentTicket, RegisteredTicket, ResultAmbientTicket, ResultTicket, CallbackEntryTicket, CallbackExitCapturedTicket, CallbackExitAmbientTicket;
        internal abstract string Kind
        {
            get;
        }
        internal ValueTask Void => new(this, 0);
        internal abstract void Complete();
        internal abstract void Fail(Exception error);
        public abstract bool GetResult(short token);
        public abstract ValueTaskSourceStatus GetStatus(short token);
        public abstract void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags);
        void IValueTaskSource.GetResult(short token) => GetResult(token);

        internal object Snapshot() => new
        {
            kind = Kind,
            statusChecks = StatusChecks,
            returnedStatuses = returnedStatuses.ToArray(),
            registrations = Registrations,
            consumptions = Consumptions,
            callbacks = Callbacks,
            inlineCallbacks = InlineCallbacks,
            useSchedulingContext = false,
            ticket = new
            {
                id = Ticket.Id,
                phase = Ticket.Phase,
                generation = Ticket.Generation
            },
            issuedParentTicket = IssuedParentTicket,
            registrationAmbientTicket = RegistrationParentTicket,
            registeredTicket = RegisteredTicket,
            resultAmbientTicket = ResultAmbientTicket,
            resultTicket = ResultTicket,
            callbackEntryTicket = CallbackEntryTicket,
            callbackExitCapturedTicket = CallbackExitCapturedTicket,
            callbackExitAmbientTicket = CallbackExitAmbientTicket
        };

        internal Perf03ProtocolAttribution.TicketScope Issue()
        {
            IssuedParentTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            return Perf03ProtocolAttribution.Enter(Ticket);
        }

        protected void Register(ValueTaskSourceOnCompletedFlags flags)
        {
            Perf03ProtocolScenario.Check(Interlocked.Increment(ref Registrations) == 1, "Raw protocol gate registered twice.");
            Perf03ProtocolScenario.Check(!flags.HasFlag(ValueTaskSourceOnCompletedFlags.UseSchedulingContext),
                "Protocol source registration used a scheduling context.");
            RegisteredTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            Perf03ProtocolScenario.Check(RegisteredTicket == Ticket.Id, "Raw registration lost its immutable operation ticket.");
        }

        protected void Invoke(Action<object?> continuation, object? state)
        {
            var captured = Ticket;
            using var attribution = Perf03ProtocolAttribution.Enter(captured);
            CallbackEntryTicket = captured.Id;
            Interlocked.Increment(ref Callbacks);
            if (Completing && CompletingThread == Environment.CurrentManagedThreadId)
                Interlocked.Increment(ref InlineCallbacks);
            try
            {
                continuation(state);
            }
            finally
            {
                CallbackExitCapturedTicket = captured.Id;
                CallbackExitAmbientTicket = Perf03ProtocolAttribution.Current.Value?.Id;
                callbackExited.SetResult(true);
            }
        }

        protected void Consume()
        {
            ResultTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            Perf03ProtocolScenario.Check(ResultTicket == Ticket.Id, "Raw GetResult lost its immutable operation ticket.");
            Perf03ProtocolScenario.Check(Interlocked.Increment(ref Consumptions) == 1, "Raw protocol result consumed twice.");
        }

        protected ValueTaskSourceStatus RecordStatus(ValueTaskSourceStatus status)
        {
            Interlocked.Increment(ref StatusChecks);
            returnedStatuses.Enqueue(status.ToString());
            return status;
        }

        internal string[] ReturnedStatuses => returnedStatuses.ToArray();
    }

    // Boolean specialization of the existing AsyncOnceGate<T> test pattern.
    internal sealed class Perf03AsyncOnceGate(bool value, Perf03ProtocolTicket ticket, string kind = "async-once") : Perf03ProtocolRawGate(ticket)
    {
        private ManualResetValueTaskSourceCore<bool> core = new() { RunContinuationsAsynchronously = true };
        internal override string Kind => kind;
        internal override void Complete() => core.SetResult(value);
        internal override void Fail(Exception error) => core.SetException(error);

        public override bool GetResult(short token)
        {
            ResultAmbientTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            using var attribution = Perf03ProtocolAttribution.Enter(Ticket);
            Consume();
            return core.GetResult(token);
        }

        public override ValueTaskSourceStatus GetStatus(short token)
        {
            return RecordStatus(core.GetStatus(token));
        }

        public override void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            RegistrationParentTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            using var attribution = Perf03ProtocolAttribution.Enter(Ticket);
            Register(flags);
            core.OnCompleted(s => Invoke(continuation, s), state, token, flags);
        }
    }

    internal sealed class Perf03RegistrationGate(string completion, Perf03ProtocolTicket ticket) : Perf03ProtocolRawGate(ticket)
    {
        private Action<object?>? continuation;
        private object? state;
        private bool completed;
        internal override string Kind => completion;

        public override ValueTaskSourceStatus GetStatus(short token)
        {
            if (completed)
                return RecordStatus(ValueTaskSourceStatus.Succeeded);
            if (completion == "before-registration")
                completed = true;
            return RecordStatus(ValueTaskSourceStatus.Pending);
        }

        public override bool GetResult(short token)
        {
            ResultAmbientTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            using var attribution = Perf03ProtocolAttribution.Enter(Ticket);
            Perf03ProtocolScenario.Check(completed, "Raw registration result must complete before consumption.");
            Consume();
            return true;
        }

        public override void OnCompleted(Action<object?> callback, object? callbackState, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            RegistrationParentTicket = Perf03ProtocolAttribution.Current.Value?.Id;
            using var attribution = Perf03ProtocolAttribution.Enter(Ticket);
            Register(flags);
            if (completed)
            {
                Completing = true;
                CompletingThread = Environment.CurrentManagedThreadId;
                try
                {
                    Invoke(callback, callbackState);
                }
                finally
                {
                    Completing = false;
                }
                return;
            }
            continuation = callback;
            state = callbackState;
            if (completion == "inside-registration")
                Complete();
        }

        internal override void Complete()
        {
            Perf03ProtocolScenario.Check(!completed && continuation is not null, "Complete one registered raw operation.");
            completed = true;
            var callback = continuation!;
            var callbackState = state;
            continuation = null;
            state = null;
            Completing = true;
            CompletingThread = Environment.CurrentManagedThreadId;
            try
            {
                Invoke(callback, callbackState);
            }
            finally
            {
                Completing = false;
            }
        }

        internal override void Fail(Exception error) => throw new InvalidOperationException("The registration-race probe does not inject failures.", error);
    }
}
