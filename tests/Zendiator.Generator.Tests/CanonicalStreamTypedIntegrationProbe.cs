namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamTypedIntegrationProbe
{
    internal object Request = null!;
    internal readonly List<string> Events = [];
    internal readonly List<(string Role, int Id)> Created = [];
    internal readonly List<object> Instances = [];
    internal int SourceEnumerations, Starts, Moves, Reads, SourceDisposals, SessionDisposals, FactoryDisposals;
    public void Resolve(string role) => Events.Add("DI.Resolve:" + role);
    public void DisposeFactory(string role) { FactoryDisposals++; Events.Add("Factory.Dispose:" + role); }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != default) throw new InvalidOperationException("Typed integration request/token changed."); }
    public T Create<T>(string role, object request, CancellationToken token, Func<int, T> make) where T : class
    { Check(request, token); var id = Created.Count + 1; var result = make(id); Created.Add((role, id)); Instances.Add(result); Events.Add("Session.Create:" + role + ":" + id); return result; }
    public ValueTask DisposeSession(string role, int id)
    { SessionDisposals++; Events.Add("Session.Dispose:" + role + ":" + id); return default; }
    public IAsyncEnumerable<T> Start<T>(object request, T item, CancellationToken token)
    { Check(request, token); Starts++; Events.Add("H.Start"); return new Source<T>(this, item); }
    public T MapOpen<T>(object request, T item, CancellationToken token)
    { Check(request, token); Events.Add("Map:open"); return item; }
    public long MapLarge(object request, int item, CancellationToken token)
    { Check(request, token); if (item != 7) throw new InvalidOperationException(); Events.Add("Map:int7->large700"); return item * 100L; }
    public long MapFinal(object request, long item, CancellationToken token)
    { Check(request, token); if (item != 700) throw new InvalidOperationException(); Events.Add("Map:large700->large701"); return item + 1; }
    public void Wrap(object request, CancellationToken token) { Check(request, token); Events.Add("W.Wrap.Open"); }
    private sealed class Source<T>(CanonicalStreamTypedIntegrationProbe p, T item) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private int _index;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.SourceEnumerations++; p.Events.Add("H.Get"); return this; }
        public T Current { get { p.Reads++; p.Events.Add("H.Current"); return item; } }
        public ValueTask<bool> MoveNextAsync() { p.Moves++; var next = _index++ == 0; p.Events.Add("H.Move:" + next); return new(next); }
        public ValueTask DisposeAsync() { p.SourceDisposals++; p.Events.Add("H.Dispose"); return default; }
    }
}
