using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private void EmitInterface(StringBuilder b)
    {
        b.AppendLine("""
            /// <summary>Typed request dispatch for this composition.</summary>
            [global::System.CodeDom.Compiler.GeneratedCode("Zendiator.SourceGenerator", "0.2.0")]
            public interface IZendiator
            {
            """);
        foreach (var route in _model.Routes.Requests.Single)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request through its configured pipeline.</summary>
                    {{Signature(route)}};
                """);
        }

        foreach (var notification in _model.Routes.Notifications)
        {
            b.AppendLine($$"""
                    /// <summary>Publishes the notification to subscribers in order.</summary>
                    {{NotificationSignature(notification, publish: false)}};
                    {{NotificationSignature(notification, publish: true)}};
                """);
        }

        if (_model.Routes.Notifications.Any(static n => !n.IsOpen))
        {
            b.AppendLine($$"""
                    /// <summary>Publishes a registered notification by its runtime type.</summary>
                    {{ErasedSignature(publish: false)}};
                    {{ErasedSignature(publish: true)}};
                """);
        }

        foreach (var multi in _model.Routes.Requests.Multiple)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request to every handler in order.</summary>
                    {{MultiSignature(multi)}};
                """);
        }

        foreach (var sync in _model.Routes.Synchronous.Single)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request synchronously without retaining it.</summary>
                    {{SyncSignature(sync)}};
                """);
        }

        foreach (var sync in _model.Routes.Synchronous.Multiple)
        {
            b.AppendLine($$"""
                    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>
                    {{SyncMultiSignature(sync)}};
                """);
        }

        foreach (var stream in _model.Routes.Streams)
        {
            b.AppendLine($$"""
                    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>
                    {{StreamSignature(stream)}};
                """);
        }

        b.AppendLine("""}""");
    }
}
