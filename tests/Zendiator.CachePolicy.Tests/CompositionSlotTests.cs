using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.CachePolicy.Tests;

public sealed class CompositionSlotTests
{
    [Fact]
    public void Unrelated_composition_does_not_expand_target_pages_or_split_its_capture()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(SlotFiller<>), typeof(SlotFiller<>));
        services.AddTransient<SlotFirst>();
        services.AddTransient<SlotSecond>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var filler = new ZendiatorServiceResolver<FillerComposition>(scope.ServiceProvider);
        var prime = typeof(CompositionSlotTests).GetMethod(nameof(Prime), BindingFlags.Static | BindingFlags.NonPublic)!;
        for (var first = 1; first <= 8; first++)
        {
            for (var second = 1; second <= 8; second++)
            {
                var type = typeof(Tuple<,>).MakeGenericType(
                    typeof(int).MakeArrayType(first), typeof(string).MakeArrayType(second));
                prime.MakeGenericMethod(type).Invoke(null, [filler]);
            }
        }

        var target = new ZendiatorServiceResolver<TargetComposition>(scope.ServiceProvider);
        ZendiatorServiceResolver asBase = target;
        var firstValue = target.GetRequiredService<SlotFirst>();
        Assert.Same(firstValue, asBase.GetRequiredService<SlotFirst>());
        var secondValue = asBase.GetRequiredService<SlotSecond>();
        Assert.Same(secondValue, target.GetRequiredService<SlotSecond>());

        static int PageLength(ZendiatorServiceResolver resolver)
        {
            var storage = (object?[])typeof(ZendiatorServiceResolver)
                .GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(resolver)!;
            return storage is object?[]?[] pages ? pages.Length : storage.Length == 0 ? 0 : 1;
        }

        Assert.True(PageLength(filler) >= 2);
        Assert.Equal(1, PageLength(target));
    }

    private static void Prime<T>(ZendiatorServiceResolver<FillerComposition> resolver) =>
        _ = resolver.GetRequiredService<SlotFiller<T>>();

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 32)]
    [InlineData(true, 32)]
    public async Task Captures_survive_page_growth_and_high_slot_first_use(bool reverse, int capacity)
    {
        var services = new ServiceCollection();
        var types = new List<Type>();
        for (var first = 1; first <= 8; first++)
            for (var second = 1; second <= 8; second++)
            {
                var argument = typeof(Tuple<,>).MakeGenericType(typeof(int).MakeArrayType(first), typeof(string).MakeArrayType(second));
                var type = typeof(SlotFiller<>).MakeGenericType(argument);
                services.AddTransient(type);
                types.Add(type);
            }
        using var provider = services.BuildServiceProvider();
        var method = typeof(ZendiatorServiceResolver<PromotionComposition>).GetMethod("GetRequiredService")!;
        var calls = types.Select(type => method.MakeGenericMethod(type)).ToArray();
        var primer = new ZendiatorServiceResolver<PromotionComposition>(provider);
        foreach (var call in calls) call.Invoke(primer, null);
        if (reverse) Array.Reverse(calls);
        var resolver = new ZendiatorServiceResolver<PromotionComposition>(provider, capacity);
        var captured = new object?[calls.Length];
        captured[0] = calls[0].Invoke(resolver, null);
        captured[1] = calls[1].Invoke(resolver, null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var finished = new ManualResetEventSlim();
        var reader = Task.Factory.StartNew(() =>
        {
            started.SetResult();
            do
            {
                Assert.Same(captured[1], calls[1].Invoke(resolver, null));
            } while (!finished.IsSet);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var index = 2; index < calls.Length; index++)
                captured[index] = calls[index].Invoke(resolver, null);
        }
        finally
        {
            finished.Set();
            await reader;
        }
        for (var index = 0; index < calls.Length; index++)
            Assert.Same(captured[index], calls[index].Invoke(resolver, null));
    }

    private sealed class FillerComposition;
    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Invalid_initial_capacity_is_rejected(int capacity)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZendiatorServiceResolver(provider, capacity));
    }

    [Fact]
    public void Small_page_grows_for_dependencies_outside_the_generated_composition()
    {
        using var provider = new ServiceCollection().AddTransient<SlotFirst>().AddTransient<SlotSecond>().AddTransient<SlotThird>().BuildServiceProvider();
        var resolver = new ZendiatorServiceResolver<BoundedComposition>(provider, 2);
        var first = resolver.GetRequiredService<SlotFirst>();
        var second = resolver.GetRequiredService<SlotSecond>();
        var field = typeof(ZendiatorServiceResolver).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(2, ((object[])field.GetValue(resolver)!).Length);
        var third = resolver.GetRequiredService<SlotThird>();
        Assert.Equal(32, ((object[])field.GetValue(resolver)!).Length);
        Assert.Same(first, resolver.GetRequiredService<SlotFirst>());
        Assert.Same(second, resolver.GetRequiredService<SlotSecond>());
        Assert.Same(third, resolver.GetRequiredService<SlotThird>());
    }

    [Fact]
    public void Reentrant_factory_can_populate_a_bounded_page_before_the_outer_capture()
    {
        ZendiatorServiceResolver<BoundedReentrantComposition> resolver = null!;
        SlotSecond? second = null;
        SlotThird? third = null;
        using var provider = new ServiceCollection().AddTransient<SlotFirst>(_ =>
        {
            second = resolver.GetRequiredService<SlotSecond>();
            third = resolver.GetRequiredService<SlotThird>();
            return new SlotFirst();
        }).AddTransient<SlotSecond>().AddTransient<SlotThird>().BuildServiceProvider();
        resolver = new(provider, 2);
        var first = resolver.GetRequiredService<SlotFirst>();
        Assert.Same(first, resolver.GetRequiredService<SlotFirst>());
        Assert.Same(second, resolver.GetRequiredService<SlotSecond>());
        Assert.Same(third, resolver.GetRequiredService<SlotThird>());
    }

    private sealed class BoundedComposition;
    private sealed class BoundedReentrantComposition;
    private sealed class TargetComposition;
    private sealed class PromotionComposition;
    private sealed class SlotFiller<T> { public SlotFiller() { } }
    private sealed class SlotFirst { public SlotFirst() { } }
    private sealed class SlotSecond { public SlotSecond() { } }
    private sealed class SlotThird { public SlotThird() { } }
}
