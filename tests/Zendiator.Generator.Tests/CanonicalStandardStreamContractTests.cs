using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData("Typed")]
    [InlineData("Inherited")]
    [InlineData("Explicit")]
    [InlineData("AliasExplicit")]
    [InlineData("DefaultLegacy")]
    public async Task Standard_stream_contract_runs_typed_only_middleware_and_preserves_fallback_dispatch(string shape)
    {
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<" + shape + ">());",
            """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Probe{public int Generic,Opened,Closed;}
            [ZendiatorHandler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>{
                public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){p.Opened++;return new(new Leaf(p));}
            }
            public sealed class Leaf(Probe p):IAsyncEnumerator<int>{private int i;public int Current=>42;
                public ValueTask<bool> MoveNextAsync()=>new(++i==1);public ValueTask DisposeAsync(){p.Closed++;return default;}}
            public sealed class Typed(Probe p):IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;return next.OpenAsync(r,t);}}
            public class Base(Probe p):IStreamMiddleware<Items,int>{
                public virtual ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    where TNext:struct,IStreamUpstream<Items,int>{p.Generic++;return next.OpenAsync(r,t);}}
            public sealed class Inherited(Probe p):Base(p);
            public interface IDefaultLegacy:IStreamMiddleware<Items,int>{
                ValueTask<IAsyncEnumerator<int>> IStreamMiddleware<Items,int>.OpenAsync(Items r,IStreamUpstream<Items,int> next,CancellationToken t)=>next.OpenAsync(r,t);}
            public sealed class DefaultLegacy:IDefaultLegacy;
            public sealed class AliasExplicit(Probe p):IStructStreamMiddleware<Items,int>{
                ValueTask<IAsyncEnumerator<int>> IStructStreamMiddleware<Items,int>.OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    {p.Generic++;return next.OpenAsync(r,t);}}
            public sealed class Explicit(Probe p):IStreamMiddleware<Items,int>{
                ValueTask<IAsyncEnumerator<int>> IStreamMiddleware<Items,int>.OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    {p.Generic++;return next.OpenAsync(r,t);}}
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            var values=new List<int>();await foreach(var value in mediator.StreamAsync(new Items()))values.Add(value);
            Check.That(values.SequenceEqual(new[]{42})&&p.Generic==__GENERIC__&&p.Opened==1&&p.Closed==1,"typed-only standard middleware preserves values and cleanup");
            """.Replace("__GENERIC__", shape == "DefaultLegacy" ? "0" : "1", StringComparison.Ordinal));
        await RunNativeStreamFixture("standard-stream-" + shape, source);
    }

    [Fact]
    public void Standard_stream_contract_requires_one_actual_open_implementation()
    {
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Empty>());",
            """
            public sealed record Items:IStreamRequest<int>;
            [ZendiatorHandler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
            public sealed class Leaf:IAsyncEnumerator<int>{public int Current=>0;public ValueTask<bool> MoveNextAsync()=>new(false);public ValueTask DisposeAsync()=>default;}
            public sealed class Empty:IStreamMiddleware<Items,int>;
            """, "await Task.CompletedTask;");
        var result = SyncDriver().RunGenerators(SyncCompilation(source, "StandardStreamEmpty")).GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0025" && d.GetMessage().Contains("must implement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Standard_stream_interface_adapters_work_in_both_directions_without_recursion()
    {
        var request = new StandardRequest();
        var typed = new StandardOnly();
        IStreamMiddleware<StandardRequest, int> typedContract = typed;
        await using (await typedContract.OpenAsync(request, (IStreamUpstream<StandardRequest, int>)new StandardPort(), default)) { }
        await using (await typedContract.OpenAsync(request, new StandardPort(), default)) { }
        Assert.Equal(2, typed.Calls);
        var legacy = new LegacyOnly();
        IStreamMiddleware<StandardRequest, int> legacyContract = legacy;
        await using (await legacyContract.OpenAsync(request, new StandardPort(), default)) { }
        Assert.Equal(1, legacy.Calls);
        IStreamMiddleware<StandardRequest, int> empty = new EmptyStandard();
        Assert.Throws<NotSupportedException>(() => empty.OpenAsync(request, (IStreamUpstream<StandardRequest, int>)new StandardPort(), default));
    }

    private sealed record StandardRequest : IStreamRequest<int>;
    private readonly struct StandardPort : IStreamUpstream<StandardRequest, int>
    {
        public ValueTask<IAsyncEnumerator<int>> OpenAsync(StandardRequest request, CancellationToken cancellationToken) => new(new StandardLeaf());
    }
    private sealed class StandardLeaf : IAsyncEnumerator<int>
    {
        public int Current => 42;
        public ValueTask<bool> MoveNextAsync() => new(false);
        public ValueTask DisposeAsync() => default;
    }
    private sealed class StandardOnly : IStreamMiddleware<StandardRequest, int>
    {
        public int Calls;
        public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(StandardRequest request, TNext next, CancellationToken cancellationToken)
            where TNext : struct, IStreamUpstream<StandardRequest, int>
        {
            Calls++;
            return next.OpenAsync(request, cancellationToken);
        }
    }
    private sealed class LegacyOnly : IStreamMiddleware<StandardRequest, int>
    {
        public int Calls;
        public ValueTask<IAsyncEnumerator<int>> OpenAsync(StandardRequest request, IStreamUpstream<StandardRequest, int> next, CancellationToken cancellationToken)
        {
            Calls++;
            return next.OpenAsync(request, cancellationToken);
        }
    }
    private sealed class EmptyStandard : IStreamMiddleware<StandardRequest, int>;
}
