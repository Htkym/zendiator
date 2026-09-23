using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string Name(EmissionType type) => type.Name;
    private static string OpenTypeofName(EmissionType type) => type.OpenName;
    private static string TypeParameters(EmissionMultiRoute route) => route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
    private static string TypeConstraints(EmissionMultiRoute route) => route.IsOpen ? string.Concat(route.MethodConstraints) : "";
    private static void Guard(StringBuilder b, EmissionRoute route, string indent)
    {
        if (route.Request.IsReferenceType)
            b.AppendLine($$"""{{indent}}global::System.ArgumentNullException.ThrowIfNull(request);""");
        b.AppendLine($$"""{{indent}}if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
    }

    private static void HandlerCall(StringBuilder b, EmissionRoute route, string services, string indent)
    {
        b.AppendLine($$"""
            {{indent}}return (({{HandlerContract(route)}}){{services}}.GetRequiredService<{{Name(route.Handler)}}>()).HandleAsync(request, cancellationToken);
            """);
    }

    private static void HandlerCallDirect(StringBuilder b, EmissionRoute route)
    {
        b.AppendLine($$"""
                        return services.GetRequiredService<{{Name(route.Handler)}}>().HandleAsync(request, cancellationToken);
            """);
    }

    private static string ServiceReceiver(string serviceType, string contract, bool direct, string services = "services") => direct ? $$"""{{services}}.GetRequiredService<{{serviceType}}>()""" : $$"""(({{contract}}){{services}}.GetRequiredService<{{serviceType}}>())""";
    private static string TypeParameters(EmissionRoute route) => route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
    private static string TypeConstraints(EmissionRoute route) => route.IsOpen ? string.Concat(route.MethodConstraints) : "";
}
