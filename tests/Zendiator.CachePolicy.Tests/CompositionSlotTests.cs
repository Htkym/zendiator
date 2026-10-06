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

        static int PageLength(ZendiatorServiceResolver resolver) =>
            ((Array)typeof(ZendiatorServiceResolver)
                .GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(resolver)!).Length;

        Assert.True(PageLength(filler) >= 2);
        Assert.Equal(1, PageLength(target));
    }

    private static void Prime<T>(ZendiatorServiceResolver<FillerComposition> resolver) =>
        _ = resolver.GetRequiredService<SlotFiller<T>>();

    private sealed class FillerComposition;
    private sealed class TargetComposition;
    private sealed class SlotFiller<T> { public SlotFiller() { } }
    private sealed class SlotFirst { public SlotFirst() { } }
    private sealed class SlotSecond { public SlotSecond() { } }
}
