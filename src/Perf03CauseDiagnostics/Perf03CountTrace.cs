namespace Competitive.Perf03.Counted;

internal sealed class Perf03CountTrace
{
    private static readonly AsyncLocal<Ticket?> Current = new();
    private static readonly Dictionary<string, string> Sites = Perf03CountSites.Sites
        .ToDictionary(site => site.Id, site => site.Kind, StringComparer.Ordinal);
    private readonly object sync = new();
    private readonly List<TraceEvent> events = [];
    private readonly Dictionary<object, int> owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, string> ownerTypes = [];
    private int publications;
    private TaskCompletionSource? publicationDrain;

    internal sealed record Ticket(Perf03CountTrace Recorder, int Operation, string Phase);
    internal sealed record TraceEvent(int Sequence, int Operation, string Phase, string Site,
        string Kind, bool? Observed, int? Owner);

    internal IDisposable Operation(int operation, string phase)
    {
        var previous = Current.Value;
        Current.Value = new(this, operation, phase);
        return new Scope(() => Current.Value = previous);
    }

    internal static Ticket? Capture() => Current.Value;

    internal static void Hit(string site, string kind, Ticket? ticket = null, object? owner = null) =>
        Record(site, kind, null, ticket, owner);

    internal static bool Status(bool observed, string site, string decisionKind)
    {
        Record(site, decisionKind, observed, null, null);
        return observed;
    }

    // These markers follow successful GetResult returns; throwing attempts are outside this count.
    internal static bool Result(bool observed, string site, string resultKind)
    {
        Record(site, resultKind, observed, null, null);
        return observed;
    }

    private static void Record(string site, string kind, bool? observed, Ticket? ticket, object? owner)
    {
        if (!Sites.TryGetValue(site, out var mapped) || mapped != kind)
            throw new InvalidOperationException("Unmapped counted source site or kind: " + site + "/" + kind);
        var operation = ticket ?? Current.Value
            ?? throw new InvalidOperationException("Counted routes require an explicit preflight operation.");
        operation.Recorder.Add(operation, site, kind, observed, owner);
    }

    private void Add(Ticket ticket, string site, string kind, bool? observed, object? owner)
    {
        lock (sync)
        {
            int? ownerId = null;
            if (owner is not null)
            {
                if (!owners.TryGetValue(owner, out var id))
                {
                    id = owners.Count + 1;
                    owners.Add(owner, id);
                    ownerTypes.Add(id, owner.GetType().FullName ?? owner.GetType().Name);
                }
                ownerId = id;
            }
            events.Add(new(events.Count + 1, ticket.Operation, ticket.Phase, site, kind, observed, ownerId));
        }
    }

    internal static IDisposable Publication(Ticket? ticket)
    {
        var recorder = (ticket ?? Current.Value)?.Recorder
            ?? throw new InvalidOperationException("Publication has no counted operation.");
        lock (recorder.sync)
        {
            if (recorder.publications++ == 0)
                recorder.publicationDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return new Scope(() =>
        {
            TaskCompletionSource? completed = null;
            lock (recorder.sync)
                if (--recorder.publications == 0)
                    completed = recorder.publicationDrain;
            completed?.SetResult();
        });
    }

    internal Task QuiesceAsync()
    {
        lock (sync)
            return publications == 0 ? Task.CompletedTask : publicationDrain!.Task;
    }

    internal TraceEvent[] Peek()
    {
        lock (sync)
            return events.ToArray();
    }

    internal (TraceEvent[] Events, Dictionary<int, string> Owners) Snapshot()
    {
        lock (sync)
        {
            if (publications != 0)
                throw new InvalidOperationException("Counted publication callbacks have not retired.");
            return (events.ToArray(), new(ownerTypes));
        }
    }

    internal static void CheckMap(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != Perf03CountSites.MapSha256)
            throw new InvalidOperationException("Counted source map differs from the compiled catalog.");
        using var document = System.Text.Json.JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("protocol").GetString() != "zendiator-perf03-counted-sites-v1")
            throw new InvalidOperationException("Invalid counted source map protocol.");
        var remaining = new Dictionary<string, string>(Sites, StringComparer.Ordinal);
        foreach (var row in root.GetProperty("sites").EnumerateArray())
            if (!remaining.Remove(row.GetProperty("id").GetString()!, out var kind) ||
                kind != row.GetProperty("kind").GetString())
                throw new InvalidOperationException("Counted source map has a duplicate or substituted site.");
        if (remaining.Count != 0)
            throw new InvalidOperationException("Counted source map is incomplete.");
    }

    internal bool CheckRejection()
    {
        using var operation = Operation(-2, "catalog-rejection-probe");
        bool unknown = false, mismatch = false;
        try
        {
            Hit("unmapped-preflight-probe", "unmapped-preflight-probe");
        }
        catch (InvalidOperationException) { unknown = true; }
        var known = Sites.First();
        try
        {
            Hit(known.Key, "mismatched-preflight-kind");
        }
        catch (InvalidOperationException) { mismatch = true; }
        if (!unknown || !mismatch || Snapshot().Events.Length != 0)
            throw new InvalidOperationException("Counted map rejection was not enforced.");
        return true;
    }

    private sealed class Scope(Action close) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                close();
            }
        }
    }
}
