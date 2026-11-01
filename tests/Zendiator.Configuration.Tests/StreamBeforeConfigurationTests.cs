using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.Configuration.Tests;

public sealed class StreamBeforeConfigurationTests
{
    private sealed class Request;
    private sealed class First;
    private sealed class Second;

    [Fact]
    public void Stream_before_snapshot_copies_and_replays_the_order_without_changing_empty_fingerprints()
    {
        var empty = new ZendiatorConfiguration().Snapshot();
        Assert.Empty(empty.StreamBefore);
        Assert.Equal("ns=;assemblies=;behaviors=;notifications=;orders=", empty.GetFingerprint());

        var types = new[] { typeof(First), typeof(Second) };
        var configuration = new ZendiatorConfiguration();
        configuration.StreamBefore<Request>(types);
        types[0] = typeof(Second);
        var snapshot = configuration.Snapshot();
        var entry = Assert.Single(snapshot.StreamBefore);
        Assert.Equal(typeof(Request), entry.Request);
        Assert.Equal(new[] { typeof(First), typeof(Second) }, entry.BeforeTypes);
        Assert.Throws<InvalidOperationException>(() => configuration.StreamBefore<Request>(typeof(First)));

        var replay = new ZendiatorConfiguration();
        replay.StreamBefore(entry.Request, entry.BeforeTypes.ToArray());
        Assert.Equal(snapshot.GetFingerprint(), replay.Snapshot().GetFingerprint());

        var reordered = new ZendiatorConfiguration();
        reordered.StreamBefore<Request>(typeof(Second), typeof(First));
        Assert.NotEqual(snapshot.GetFingerprint(), reordered.Snapshot().GetFingerprint());
    }
}
