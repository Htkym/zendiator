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

    // Only the trace ticket flows here; user ExecutionContext is not captured or restored.
    internal static IDisposable Enter(Ticket? ticket)
    {
        if (ticket is null)
            throw new InvalidOperationException("Counted continuation has no captured operation ticket.");
        var previous = Current.Value;
        Current.Value = ticket;
        return new Scope(() => Current.Value = previous);
    }

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
        string variant = global::Competitive.Perf03.Diagnostics.Perf03CauseVariant.Id;
        bool control = variant == "V0";
        if (variant is not ("V0" or "VEC" or "VEntry" or "VRoot" or "VWord") ||
            root.GetProperty("schemaVersion").GetInt32() != (control ? 1 : 2) ||
            root.GetProperty("protocol").GetString() != (control
                ? "zendiator-perf03-counted-sites-v1" : "zendiator-perf03-variant-counted-sites-v2") ||
            root.GetProperty("variant").GetString() != variant)
            throw new InvalidOperationException("Invalid or substituted counted source map variant/protocol.");
        if (!control)
            CheckCandidateFiles(root, Path.GetDirectoryName(Path.GetFullPath(path))!, variant);
        var remaining = new Dictionary<string, string>(Sites, StringComparer.Ordinal);
        foreach (var row in root.GetProperty("sites").EnumerateArray())
            if (!remaining.Remove(row.GetProperty("id").GetString()!, out var kind) ||
                kind != row.GetProperty("kind").GetString())
                throw new InvalidOperationException("Counted source map has a duplicate or substituted site.");
        if (remaining.Count != 0)
            throw new InvalidOperationException("Counted source map is incomplete.");
    }

    private static void CheckCandidateFiles(System.Text.Json.JsonElement map, string directory, string variant)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Candidate counted directory cannot be a reparse point.");
        if (map.GetProperty("referenceVariant").GetString() != (variant == "VWord" ? "VRoot" : "V0"))
            throw new InvalidOperationException("Counted treatment reference variant changed.");
        string baseline = CheckBinding(map.GetProperty("baselineMap"));
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(baseline)))
        {
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("protocol").GetString() != "zendiator-perf03-counted-sites-v1" ||
                root.GetProperty("variant").GetString() != "V0")
                throw new InvalidOperationException("The archived baseline counted map is not V0.");
        }
        string provenance = CheckBinding(map.GetProperty("snapshotProvenance"));
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(provenance)))
        {
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("protocol").GetString() != "zendiator-perf03-snapshot-v1" ||
                root.GetProperty("variant").GetString() != "V0")
                throw new InvalidOperationException("The preserved snapshot provenance is not V0.");
        }
        CheckBinding(map.GetProperty("treatmentPatch"));
        CheckBinding(map.GetProperty("instrumentationPatch"));
        var trace = map.GetProperty("traceBinding");
        CheckBinding(trace);
        CheckRelative(trace.GetProperty("referencePath").GetString()!, trace.GetProperty("referenceSha256").GetString()!);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "Counted.Stream0.g.cs", "Counted.Relay5.g.cs", "CountedStreamCursorState.cs"
        };
        foreach (var file in map.GetProperty("files").EnumerateArray())
        {
            string name = file.GetProperty("file").GetString()!;
            if (!expected.Remove(name))
                throw new InvalidOperationException("Counted candidate file set is duplicate or substituted.");
            CheckRelative(name, file.GetProperty("sha256").GetString()!);
            string origin = file.GetProperty("originFile").GetString()!;
            if (Path.GetFileName(origin) != origin)
                throw new InvalidOperationException("Current timing source must be a diagnostic basename.");
            string originHash = file.GetProperty("originSha256").GetString()!;
            CheckRelative(origin, originHash);
            CheckRelative(file.GetProperty("originPath").GetString()!, originHash);
            CheckRelative(file.GetProperty("referencePath").GetString()!, file.GetProperty("referenceSha256").GetString()!);
        }
        if (expected.Count != 0)
            throw new InvalidOperationException("Counted candidate file set is incomplete.");

        string CheckBinding(System.Text.Json.JsonElement binding) =>
            CheckRelative(binding.GetProperty("path").GetString()!, binding.GetProperty("sha256").GetString()!);

        string CheckRelative(string relative, string expectedHash)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) ||
                relative.Split(['/', '\\']).Any(part => part is "" or "." or "..") ||
                expectedHash.Length != 64 || expectedHash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
                throw new InvalidOperationException("Invalid candidate counted archive path or hash.");
            string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Candidate counted archive escaped its diagnostic directory.");
            for (string? current = full; current is not null && current.Length >= parent.Length;
                current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Candidate counted archives cannot use reparse paths.");
            if ((File.GetAttributes(full) & FileAttributes.Directory) != 0 ||
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(full))) != expectedHash)
                throw new InvalidOperationException("Candidate counted source/archive hash changed.");
            return full;
        }
    }

    internal bool CheckRejection()
    {
        using var operation = Operation(-2, "catalog-rejection-probe");
        bool unknown = false, mismatch = false;
        try
        {
            Hit("unmapped-preflight-probe", "unmapped-preflight-probe");
        }
        catch (InvalidOperationException)
        {
            unknown = true;
        }
        var known = Sites.First();
        try
        {
            Hit(known.Key, "mismatched-preflight-kind");
        }
        catch (InvalidOperationException)
        {
            mismatch = true;
        }
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
