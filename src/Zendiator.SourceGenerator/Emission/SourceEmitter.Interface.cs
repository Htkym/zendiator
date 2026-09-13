using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static void EmitInterface(
        StringBuilder b,
        List<Route> routes,
        List<NotificationRoute> notifications,
        List<MultiRoute> multiRoutes,
        List<Route> syncRoutes,
        List<MultiRoute> syncMultiRoutes,
        List<Route> streamRoutes)
    {
        b.AppendLine("""
            /// <summary>Typed request dispatch for this composition.</summary>
            public interface IZendiator
            {
            """);
        foreach (var route in routes)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request through its configured pipeline.</summary>
                    {{Signature(route)}};
                """);
        }

        foreach (var notification in notifications)
        {
            b.AppendLine($$"""
                    /// <summary>Publishes the notification to subscribers in order.</summary>
                    {{NotificationSignature(notification, publish: false)}};
                    {{NotificationSignature(notification, publish: true)}};
                """);
        }

        if (notifications.Any(static n => !n.IsOpen))
        {
            b.AppendLine($$"""
                    /// <summary>Publishes a registered notification by its runtime type.</summary>
                    {{ErasedSignature(publish: false)}};
                    {{ErasedSignature(publish: true)}};
                """);
        }

        foreach (var multi in multiRoutes)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request to every handler in order.</summary>
                    {{MultiSignature(multi)}};
                """);
        }

        foreach (var sync in syncRoutes)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request synchronously without retaining it.</summary>
                    {{SyncSignature(sync)}};
                """);
        }

        foreach (var sync in syncMultiRoutes)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>
                    {{SyncMultiSignature(sync)}};
                """);
        }

        foreach (var stream in streamRoutes)
        {
            b.AppendLine($$"""
                    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>
                    {{StreamSignature(stream)}};
                """);
        }

        b.AppendLine("""}""");
    }
}
