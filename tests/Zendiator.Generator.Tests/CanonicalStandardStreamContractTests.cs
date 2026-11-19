using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            [Handler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>{
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
    public async Task Forwarding_stream_chain_preserves_pending_open_context_cancellation_and_disposal()
    {
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Relay<Inner>>(),Stage.Wrapper<Relay<Outer>>());",
            """
            public sealed record Items:IStreamRequest<int>;
            public sealed class Outer; public sealed class Inner;
            public sealed class Probe{
                public readonly AsyncLocal<string?> Ambient=new();
                public readonly TaskCompletionSource Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public readonly List<string> Calls=new();public CancellationTokenSource? Cancel;public int Disposals;
                public void Enter(string stage){Calls.Add(stage);Ambient.Value=stage;Cancel?.Cancel();}
            }
            public sealed class Relay<T>(Probe p):IStreamMiddleware<Items,int>{
                private readonly string stage=typeof(T).Name;
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    where TNext:struct,IStreamUpstream<Items,int>{p.Enter(stage);return next.OpenAsync(r,t);}
            }
            [Handler]public sealed class Handler(Probe p):IStreamSourceHandler<Items,int,Leaf>{
                public async ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){
                    Check.That(p.Ambient.Value=="Inner","handler sees inner preprocessing");p.Calls.Add("handler");
                    await p.Ready.Task;return new Leaf(p,t);}
            }
            public sealed class Leaf(Probe p,CancellationToken t):IAsyncEnumerator<int>{
                public int Current=>42;
                public ValueTask<bool> MoveNextAsync(){t.ThrowIfCancellationRequested();Check.That(p.Ambient.Value=="Outer","first move sees the outer opening context");return new(true);}
                public ValueTask DisposeAsync(){p.Disposals++;return default;}
            }
            """, """
            var p=new Probe();var services=new ServiceCollection();services.AddSingleton(p);Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            using var api=new CancellationTokenSource();using var enumeration=new CancellationTokenSource();
            var source=mediator.StreamAsync(new Items(),api.Token);
            await using(var cursor=source.GetAsyncEnumerator(enumeration.Token)){
                Check.That(p.Calls.Count==0,"lazy open");var pending=cursor.MoveNextAsync();
                Check.That(!pending.IsCompleted&&p.Ambient.Value is null,"pending open restores caller context");p.Ready.SetResult();
                Check.That(await pending&&cursor.Current==42,"first item");}
            Check.That(p.Calls.SequenceEqual(new[]{"Outer","Inner","handler"})&&p.Disposals==1,"forward in order and close once");
            p.Calls.Clear();p.Cancel=enumeration;
            await using(var cursor=source.GetAsyncEnumerator(enumeration.Token)){
                try{await cursor.MoveNextAsync();throw new InvalidOperationException("Expected cancellation");}
                catch(OperationCanceledException){}}
            Check.That(p.Calls.SequenceEqual(new[]{"Outer"})&&p.Disposals==1,"cancellation checked between stages");
            """);
        await RunNativeStreamFixture("forwarding-chain", source);
    }

    [Theory]
    [InlineData("return next.OpenAsync(r,t);")]
    [InlineData("var opened=next.OpenAsync(r,t);return opened;")]
    public async Task Forwarding_stream_uses_cursor_returned_by_intercepted_middleware(string forward)
    {
        string[] markers = ["byte", "short", "int", "long", "char", "float", "double"];
        var wrappers = string.Join(",", markers.Select(marker => $"Stage.Wrapper<Relay<{marker}>>()"));
        var source = NativeStreamFixture(
            $"c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,{wrappers});",
            $$"""
            public sealed record Items:IStreamRequest<int>;
            public static class Probe { public static int Opens,LeafDisposals,DecoratorDisposals; }
            public sealed class Relay<T>:IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    where TNext:struct,IStreamUpstream<Items,int>{ {{forward}} }
            }
            [Handler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{
                public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){Probe.Opens++;return new(new Leaf());}
            }
            public sealed class Leaf:IAsyncEnumerator<int>{
                public int Current=>42;
                public ValueTask<bool> MoveNextAsync()=>new(true);
                public ValueTask DisposeAsync(){Probe.LeafDisposals++;return default;}
            }
            public sealed class Decorator(IAsyncEnumerator<int> child):IAsyncEnumerator<int>{
                public int Current=>child.Current+1;
                public ValueTask<bool> MoveNextAsync()=>child.MoveNextAsync();
                public ValueTask DisposeAsync(){Probe.DecoratorDisposals++;return child.DisposeAsync();}
            }
            """, $$"""
            var services=new ServiceCollection();Register(services);
            await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
            var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            await foreach(var value in mediator.StreamAsync(new Items())){
                Check.That(value==42+{{markers.Length}},"every decorator contributes to Current");
                break;
            }
            Check.That(Probe.Opens==1&&Probe.LeafDisposals==1&&Probe.DecoratorDisposals=={{markers.Length}},
                "early break disposes all acquired cursors exactly once");
            """);
        await RunNativeStreamFixture("intercepted-forward", source, additionalGenerator: new CursorDecoratorGenerator().AsSourceGenerator());
    }

    private sealed class CursorDecoratorGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
            {
                var tree = compilation.SyntaxTrees.Single();
                var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Single(invocation => invocation.Expression.ToString() == "next.OpenAsync");
                var location = compilation.GetSemanticModel(tree).GetInterceptableLocation(call)!;
                output.AddSource("CursorDecoratorInterceptor.g.cs", $$"""
                    using System.Collections.Generic;
                    using System.Threading;
                    using System.Threading.Tasks;
                    namespace System.Runtime.CompilerServices {
                        file sealed class InterceptsLocationAttribute : System.Attribute {
                            public InterceptsLocationAttribute(int version, string data) { }
                        }
                    }
                    namespace Zendiator.Generated.Interceptors {
                        file static class CursorDecoratorInterceptor {
                            [System.Runtime.CompilerServices.InterceptsLocationAttribute({{location.Version}}, "{{location.Data}}")]
                            public static async ValueTask<IAsyncEnumerator<int>> Decorate(
                                this IStreamUpstream<NativeStream.Items,int> next, NativeStream.Items r, CancellationToken t) =>
                                new NativeStream.Decorator(await next.OpenAsync(r,t));
                        }
                    }
                    """);
            });
    }

    [Fact]
    public async Task Forwarding_stream_keeps_independent_token_changed_by_ref_extension()
    {
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Replace>());",
            """
            public sealed record Items:IStreamRequest<int>;
            public static class Replacement{
                public static readonly CancellationTokenSource Source=new();
                public static void Change(this ref CancellationToken token)=>token=Source.Token;
            }
            public sealed class Replace:IStreamMiddleware<Items,int>{
                public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items r,TNext next,CancellationToken t)
                    where TNext:struct,IStreamUpstream<Items,int>{t.Change();return next.OpenAsync(r,t);}
            }
            [Handler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{
                public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t){
                    Check.That(t==Replacement.Source.Token,"replacement reached handler");return new(new Leaf());}
            }
            public sealed class Leaf:IAsyncEnumerator<int>{
                public int Current=>42;public ValueTask<bool> MoveNextAsync()=>new(true);public ValueTask DisposeAsync()=>default;}
            """, """
            var services=new ServiceCollection();Register(services);await using var provider=services.BuildServiceProvider();
            await using var scope=provider.CreateAsyncScope();var mediator=scope.ServiceProvider.GetRequiredService<global::NativeStream.Generated.Reads.IZendiator>();
            await using var cursor=mediator.StreamAsync(new Items()).GetAsyncEnumerator();
            Check.That(await cursor.MoveNextAsync(),"first move");Replacement.Source.Cancel();
            try{await cursor.MoveNextAsync();throw new InvalidOperationException("Expected replacement cancellation");}
            catch(OperationCanceledException error){Check.That(error.CancellationToken==Replacement.Source.Token,"cancelled inner token");}
            """);
        await RunNativeStreamFixture("forwarding-replaced-token", source);
    }

    [Fact]
    public void Standard_stream_contract_requires_one_actual_open_implementation()
    {
        var source = NativeStreamFixture(
            "c.Handlers<Items>(DispatchFamily.Stream,typeof(Handler));c.Pipeline<Items>(DispatchFamily.Stream,Stage.Wrapper<Empty>());",
            """
            public sealed record Items:IStreamRequest<int>;
            [Handler]public sealed class Handler:IStreamSourceHandler<Items,int,Leaf>{public ValueTask<Leaf> OpenAsync(Items r,CancellationToken t)=>new(new Leaf());}
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
        await using (await typedContract.OpenAsync(request, (IStreamUpstream<StandardRequest, int>)new StandardPort(), default))
        {
        }
        await using (await typedContract.OpenAsync(request, new StandardPort(), default))
        {
        }
        Assert.Equal(2, typed.Calls);
        var legacy = new LegacyOnly();
        IStreamMiddleware<StandardRequest, int> legacyContract = legacy;
        await using (await legacyContract.OpenAsync(request, new StandardPort(), default))
        {
        }
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
