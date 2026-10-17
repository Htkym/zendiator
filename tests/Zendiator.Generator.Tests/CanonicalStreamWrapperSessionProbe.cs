using Zendiator;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamWrapperSessionProbe
{
    internal object Request = null!;
    internal bool NoOpen;
    internal bool OpenPerEnumeration;
    internal readonly List<string> Events = [];
    internal readonly List<(string Role, int Id)> Created = [];
    internal readonly List<object> Instances = [];
    internal readonly Dictionary<int, string> Active = new();
    internal int Opens, UpstreamEnumerations, Starts, Moves, Reads, SourceDisposals, SessionDisposals, ServiceDisposals, WrapperFinallys, InnerHeldChecks, OuterAliveChecks;
    public void Resolve(string role) => Events.Add("DI.Resolve:" + role);
    public void ReleaseService(string role) { ServiceDisposals++; Events.Add("DI.Dispose:" + role); }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != default) throw new InvalidOperationException("Wrapper session request/token changed."); }
    public T Create<T>(string role, object request, CancellationToken token, Func<int, T> make) where T : class
    {
        Check(request, token); var id = Created.Count + 1; var value = make(id); Instances.Add(value); Created.Add((role, id)); Active.Add(id, role);
        Events.Add("Session.Create:" + role + ":" + id); return value;
    }
    public ValueTask DisposeSession(string role, int id)
    {
        if (!Active.Remove(id, out var actual) || actual != role) throw new InvalidOperationException("Wrong or repeated session owner cleanup.");
        SessionDisposals++; Events.Add("Session.Dispose:" + role + ":" + id); return default;
    }
    public int Map(string role, int id, object request, int item, CancellationToken token)
    { Check(request, token); if (Active[id] != role) throw new InvalidOperationException(); Events.Add("Map:" + role + ":" + id + ":" + item + "->" + (item + 1)); return item + 1; }
    public IAsyncEnumerable<int> Start(int id, object request, CancellationToken token)
    { Check(request, token); if (Active[id] != "H") throw new InvalidOperationException(); Starts++; Events.Add("H.Start:" + id); return new Source(this, id); }
    public IAsyncEnumerable<int> Wrap<R, N>(R request, N upstream, int id, CancellationToken token) where N : IStreamUpstream<R, int>
    {
        Check(request!, token); if (Active[id] != "W") throw new InvalidOperationException(); Events.Add("W.Wrap:" + id); return Run();
        async IAsyncEnumerable<int> Run()
        {
            try
            {
                if (NoOpen) { Events.Add("W.NoOpen"); yield return 100; yield break; }
                IAsyncEnumerable<int>? envelope = null;
                if (!OpenPerEnumeration) { Opens++; Events.Add("W.Open"); envelope = upstream.Open(request, token); }
                for (var n = 0; n < 2; n++)
                {
                    if (OpenPerEnumeration) { Opens++; Events.Add("W.Open"); envelope = upstream.Open(request, token); }
                    var before = Created.Count; var cursor = envelope!.GetAsyncEnumerator(); UpstreamEnumerations++;
                    if (Created.Count != before) throw new InvalidOperationException("Inner session created before first Move.");
                    try
                    {
                        if (!await cursor.MoveNextAsync()) throw new InvalidOperationException(); var value = cursor.Current; _ = cursor.Current;
                        Events.Add("W.Item:" + value); yield return value;
                        if (await cursor.MoveNextAsync()) throw new InvalidOperationException();
                        if (Active.Values.Count(x => x is "I" or "H") != 2) throw new InvalidOperationException("Inner session ended before Dispose.");
                        InnerHeldChecks++;
                    }
                    finally { await cursor.DisposeAsync(); await cursor.DisposeAsync(); }
                    if (Active.Count != 2 || Active.Values.Count(x => x is "O" or "W") != 2) throw new InvalidOperationException("Inner Dispose released outer sessions.");
                    OuterAliveChecks++; Events.Add("W.OuterAlive");
                }
            }
            finally { WrapperFinallys++; Events.Add("W.Finally:" + id); }
        }
    }
    private sealed class Source(CanonicalStreamWrapperSessionProbe p, int id) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.Events.Add("H.Get:" + id); return this; }
        public int Current { get { p.Reads++; p.Events.Add("H.Current:" + id + ":1"); return 1; } }
        public ValueTask<bool> MoveNextAsync() { p.Moves++; var next = _index++ == 0; p.Events.Add("H.Move:" + id + ":" + next); return new(next); }
        public ValueTask DisposeAsync() { p.SourceDisposals++; p.Events.Add("H.Dispose:" + id); return default; }
    }
}
