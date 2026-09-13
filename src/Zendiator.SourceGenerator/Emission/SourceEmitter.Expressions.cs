using System.Text;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string TypeParameters(MultiRoute route) => route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";

    private static string TypeConstraints(MultiRoute route) => route.IsOpen ? string.Concat(route.MethodConstraints) : "";

    private static void Guard(StringBuilder b, Route route, string indent)
    {
        if (route.Request.IsReferenceType)
            b.AppendLine($$"""{{indent}}global::System.ArgumentNullException.ThrowIfNull(request);""");
        b.AppendLine($$"""{{indent}}cancellationToken.ThrowIfCancellationRequested();""");
    }

    private static void HandlerCall(StringBuilder b, Route route, string services, string indent)
    {
        b.AppendLine($$"""
            {{indent}}return (({{HandlerContract(route)}}){{services}}.GetRequiredService<{{Name(route.Handler)}}>()).HandleAsync(request, cancellationToken);
            """);
    }

    private static void HandlerCallDirect(StringBuilder b, Route route)
    {
        b.AppendLine($$"""
                        return services.GetRequiredService<{{Name(route.Handler)}}>().HandleAsync(request, cancellationToken);
            """);
    }

    private static string ServiceReceiver(string serviceType, string contract, bool direct, string services = "services") => direct ? $$"""{{services}}.GetRequiredService<{{serviceType}}>()""" : $$"""(({{contract}}){{services}}.GetRequiredService<{{serviceType}}>())""";
    private static string TypeParameters(Route route) => route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
    private static string TypeConstraints(Route route) => route.IsOpen ? string.Concat(route.MethodConstraints) : "";
}
