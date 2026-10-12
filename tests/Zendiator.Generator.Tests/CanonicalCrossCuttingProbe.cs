using System.Collections.Concurrent;
using System.Globalization;
using Xunit;
namespace Zendiator.Generator.Tests;

public interface ICrossRequest{string Op{get;}int Value{get;}int Stamp{get;}int Depth{get;}}
public sealed record CrossEvent(string Phase,object Request,string Op,int Stamp,int Receiver,Exception? Error,SendExitInfo? Exit,string? Ambient,string Culture,string Context,string Scheduler);
public sealed record CrossResource(int Id,string Kind,string Op,int ParentStamp,bool Returned);
public sealed class CrossSynchronizationContext(string name):SynchronizationContext
{
    public string Name{get;}=name;public int Posts;
    public override void Post(SendOrPostCallback d,object? state){Interlocked.Increment(ref Posts);ThreadPool.UnsafeQueueUserWorkItem(s=>{var old=Current;SetSynchronizationContext(this);try{d(s);}finally{SetSynchronizationContext(old);}},state);}
}
public sealed class CrossGate<T>(T value):IAsyncOnceGate
{
    private readonly AsyncOnceGate<T> _gate=new(value);public ValueTask<T> Value=>_gate.Task;public ValueTask Void=>_gate.Void;public int Consumptions=>_gate.Consumptions;public void Complete()=>_gate.Complete();public void Fail(Exception e)=>_gate.Fail(e);
}
public sealed class CrossCuttingProbe
{
    public static readonly AsyncLocal<string?> Ambient=new();
    public readonly ConcurrentQueue<CrossEvent> Events=new();
    public readonly ConcurrentDictionary<object,CrossResource> Resources=new(ReferenceEqualityComparer.Instance);
    public readonly ConcurrentDictionary<object,int> Releases=new(ReferenceEqualityComparer.Instance);
    public readonly ConcurrentDictionary<string,IAsyncOnceGate> Gates=new();
    public readonly ConcurrentQueue<string> GateOrder=new();public readonly ConcurrentDictionary<string,Exception> Failures=new();
    public readonly Exception BodyError=new("body"),FactoryError=new("factory partial"),RecoveryError=new("factory recovery");
    public readonly CancellationTokenSource Cancellation=new(),Foreign=new();
    public readonly CrossSynchronizationContext CallerContext=new("caller"),MutatedContext=new("mutated");
    public TaskScheduler EntryScheduler=TaskScheduler.Default;public string Mode="success";public bool Continuous,PendingBody,PartialFactory,RecoveryFails,ContextEnabled;
    public Func<string,ValueTask<int>>? Reenter;private int _identity;
    public int Create(object resource,string kind,ICrossRequest r){var id=Interlocked.Increment(ref _identity);Assert.True(Resources.TryAdd(resource,new(id,kind,r.Op,r.Stamp,false)));return id;}
    public void Returned(object resource)=>Resources.AddOrUpdate(resource,_=>throw new InvalidOperationException(),(_,r)=>r with{Returned=true});
    public void Check(int receiver,ICrossRequest r)=>Assert.Equal(receiver,r.Stamp);
    public void Release(object resource){Assert.True(Resources.ContainsKey(resource));Assert.True(Releases.TryAdd(resource,1));}
    private void Record(string phase,ICrossRequest r,int receiver,Exception? error,SendExitInfo? exit)
    {
        var context=SynchronizationContext.Current is CrossSynchronizationContext c?c.Name:SynchronizationContext.Current==null?"none":"other";
        var scheduler=ReferenceEquals(TaskScheduler.Current,EntryScheduler)?"entry":ReferenceEquals(TaskScheduler.Current,TaskScheduler.Default)?"default":"other";
        Events.Enqueue(new(phase,r,r.Op,r.Stamp,receiver,error,exit,Ambient.Value,CultureInfo.CurrentCulture.Name,context,scheduler));
        if(ContextEnabled){Ambient.Value=phase;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(phase switch{"k.factory"=>"fr-FR","k.before"=>"de-DE","h.factory"=>"it-IT","h.body"=>"ja-JP","h.cleanup"=>"en-GB","k.after"=>"es-ES","k.finally"=>"pt-PT",_=>"en-US"});if(phase=="k.factory")SynchronizationContext.SetSynchronizationContext(MutatedContext);}
    }
    private bool Suspends(string phase)=>Continuous||PendingBody&&phase=="h.body";
    private IAsyncOnceGate Pending<T>(string phase,ICrossRequest r,int receiver,T value)
    {var key=r.Op+"/"+receiver+"/"+phase;var gate=new CrossGate<T>(value);Assert.True(Gates.TryAdd(key,gate));GateOrder.Enqueue(key);return gate;}
    public ValueTask<T> Value<T>(string phase,ICrossRequest r,int receiver,T value,Exception? error=null,SendExitInfo? exit=null)
    {Record(phase,r,receiver,error,exit);if(Suspends(phase))return((CrossGate<T>)Pending(phase,r,receiver,value)).Value;if(Failures.TryGetValue(phase,out var failure))throw failure;return new(value);}
    public ValueTask Void(string phase,ICrossRequest r,int receiver,Exception? error=null,SendExitInfo? exit=null)
    {Record(phase,r,receiver,error,exit);if(Suspends(phase))return((CrossGate<int>)Pending(phase,r,receiver,0)).Void;if(Failures.TryGetValue(phase,out var failure))throw failure;return default;}
    public void Sync(string phase,ICrossRequest r,int receiver,Exception? error=null,SendExitInfo? exit=null)
    {Record(phase,r,receiver,error,exit);if(Failures.TryGetValue(phase,out var failure))throw failure;}
    public async Task Drain(Task task)
    {
        var cursor=0;var deadline=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){
            if(DateTime.UtcNow>deadline)throw new TimeoutException("Consecutive phase gates did not finish.");
            var keys=GateOrder.ToArray();if(cursor<keys.Length){var key=keys[cursor++];var phase=key[(key.LastIndexOf('/')+1)..];var gate=Gates[key];Assert.Equal(0,gate.Consumptions);if(Failures.TryGetValue(phase,out var error))gate.Fail(error);else gate.Complete();}
            else await Task.Delay(1).ConfigureAwait(false);
        }
        Assert.Equal(GateOrder.Count,cursor);Assert.All(Gates.Values,g=>Assert.Equal(1,g.Consumptions));
    }
    public async Task WaitForBodies(int count)
    {var deadline=DateTime.UtcNow.AddSeconds(10);while(Gates.Keys.Count(k=>k.EndsWith("/h.body",StringComparison.Ordinal))<count){if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(1).ConfigureAwait(false);}}
    public void CompleteBodies(){foreach(var g in Gates.Where(k=>k.Key.EndsWith("/h.body",StringComparison.Ordinal)).Select(k=>k.Value))g.Complete();}
}
