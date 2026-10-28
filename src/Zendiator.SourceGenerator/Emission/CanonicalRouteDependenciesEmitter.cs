using System;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalRouteDependenciesPlan(string Name, string QualifiedName, EquatableArray<string> Types);

internal static class CanonicalRouteDependenciesEmitter
{
    internal static CanonicalRouteDependenciesPlan? Plan(CanonicalSyncCompositionEmission c, BoundDispatchFamily family)
    {
        if (family != BoundDispatchFamily.SendAsync || c.RouteDependencyTypes.Count < 2) return null;
        var name = "RouteDependencies_" + CanonicalSplitSourceEmitter.Id(family + "|" + c.Handler.RequestName);
        return new(name, "global::" + c.Namespace + ".Zendiator." + name, c.RouteDependencyTypes);
    }

    internal static void Emit(StringBuilder b, CanonicalRouteDependenciesPlan? plan)
    {
        if (plan == null) return;
        b.AppendLine("internal sealed class " + plan.Name + " {");
        for (var i = 0; i < plan.Types.Count; i++)
            b.AppendLine("internal readonly " + plan.Types[i] + " Service" + i + ";");
        b.AppendLine("public " + plan.Name + "(" + string.Join(", ", plan.Types.Select((type, i) => type + " service" + i)) + ") {");
        for (var i = 0; i < plan.Types.Count; i++)
            b.AppendLine("Service" + i + " = service" + i + " ?? throw new global::System.InvalidOperationException(\"No service for type '\" + typeof(" + plan.Types[i] + ") + \"' has been registered.\");");
        b.AppendLine("}\n}");
    }

    internal static void Acquire(StringBuilder b, CanonicalRouteDependenciesPlan? plan)
    {
        if (plan != null)
            b.AppendLine("var dependencies = " + CanonicalServiceResolution.Emit(plan.QualifiedName, false) + ";");
    }

    internal static string Receiver(CanonicalRouteDependenciesPlan? plan, string type, bool isValueType)
    {
        if (plan == null) return CanonicalServiceResolution.Emit(type, isValueType);
        for (var i = 0; i < plan.Types.Count; i++)
            if (StringComparer.Ordinal.Equals(plan.Types[i], type)) return "dependencies.Service" + i;
        throw new InvalidOperationException("A route dependency carrier must contain every selected receiver.");
    }
}
