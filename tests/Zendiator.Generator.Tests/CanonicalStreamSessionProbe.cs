using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

public sealed class CanonicalStreamSessionProbe
{
    internal object Request = null!;
    internal bool FailHandler;
    internal readonly InvalidOperationException FactoryError = new("inner factory failed");
    internal readonly List<string> Events = [];
    internal readonly List<(string Role, int Id)> Created = [];
    internal readonly List<object> Instances = [];
    internal readonly List<Func<int>> Consumptions = [];
    internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Action Release = null!;
    internal int Starts, Moves, Reads, SourceDisposals, SessionDisposals, ServiceDisposals;
    public void Resolve(string role) => Events.Add("DI.Resolve:" + role);
    public void ReleaseService(string role) { ServiceDisposals++; Events.Add("DI.Dispose:" + role); }
    private void Check(object request, CancellationToken token)
    { if (!ReferenceEquals(request, Request) || token != default) throw new InvalidOperationException("Session request/token changed."); }
    public T Create<T>(string role, object request, CancellationToken token, Func<int, T> make) where T : class
    {
        Check(request, token);
        if (role == "H" && FailHandler) { Events.Add("Factory.Throw:H"); throw FactoryError; }
        var id = Created.Count + 1; var value = make(id); Created.Add((role, id)); Instances.Add(value); Events.Add("Session.Create:" + role + ":" + id); return value;
    }
    public ValueTask<T> CreateAsync<T>(string role, object request, CancellationToken token, Func<int, T> make) where T : class
    {
        Check(request, token); var gate = new ContractOnce<T>(); Consumptions.Add(() => gate.Consumptions);
        Release = () => { Events.Add("Factory.Release:" + role); gate.Complete(Create(role, request, token, make)); };
        Events.Add("Factory.Pending:" + role); Entered.TrySetResult(); return gate.Task;
    }
    public ValueTask DisposeSession(string role, int id)
    {
        if (id == 0) ReleaseService(role);
        else { SessionDisposals++; Events.Add("Session.Dispose:" + role + ":" + id); }
        return default;
    }
    public int Map(string role, int id, object request, int item, CancellationToken token)
    { Check(request, token); var result = role == "B" ? item * 10 : item + 1; Events.Add("Map:" + role + ":" + id + ":" + item + "->" + result); return result; }
    public IAsyncEnumerable<int> Start(int id, object request, CancellationToken token)
    { Check(request, token); Starts++; Events.Add("H.Start:" + id); return new Source(this, id); }
    private sealed class Source(CanonicalStreamSessionProbe p, int id) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { if (token != default) throw new InvalidOperationException(); p.Events.Add("H.Get:" + id); return this; }
        public int Current { get { p.Reads++; p.Events.Add("H.Current:" + id + ":1"); return 1; } }
        public ValueTask<bool> MoveNextAsync() { p.Moves++; var next = _index++ == 0; p.Events.Add("H.Move:" + id + ":" + next); return new(next); }
        public ValueTask DisposeAsync() { p.SourceDisposals++; p.Events.Add("H.Dispose:" + id); return default; }
    }
}
