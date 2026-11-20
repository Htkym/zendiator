using System;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalClosedServiceCacheEmitter
{
    private const int BlockSize = 16;

    internal static void AppendSharedMembers(StringBuilder b, CanonicalServiceDependency[] closed)
    {
        // An exact first receiver is its own key. A DI override can return a subtype for several distinct keys.
        b.AppendLine("""
            private object? _firstClosedService;
            private T? SharedClosedService<T>(global::System.Type type, object first, T? resolved = null) where T : class {
                if (first.GetType() == type) return (T)first;
                if (first is GenericServiceEntry entry && entry.Key == type) return (T)entry.Value;
                return resolved is null ? GenericService<T>(type) : PublishGenericService(type, resolved);
            }
            private static T MissingClosedService<T>(global::System.Type type) =>
                throw new global::System.InvalidOperationException("No service for type '" + type + "' has been registered.");
            """);
        foreach (var type in closed)
        {
            var id = Id(type);
            var storage = Storage(type);
            b.AppendLine($$"""
                internal {{storage}} Required_{{id}}() => Service_{{id}}() ?? MissingClosedService<{{storage}}>(typeof({{type.TypeName}}));
                private {{storage}}? Service_{{id}}() {
                    var first = global::System.Threading.Volatile.Read(ref _firstClosedService);
                    return first is {{storage}} cached && cached.GetType() == typeof({{type.TypeName}}) ? cached : ResolveClosed_{{id}}(first);
                }
                private {{storage}}? ResolveClosed_{{id}}(object? first) {
                    if (first is not null) return SharedClosedService<{{storage}}>(typeof({{type.TypeName}}), first);
                    var resolved = ({{storage}}?)_services.GetService(typeof({{type.TypeName}}));
                    if (resolved is null) return null;
                    object keyed = resolved.GetType() == typeof({{type.TypeName}}) ? resolved : new GenericServiceEntry(typeof({{type.TypeName}}), resolved, null);
                    first = global::System.Threading.Interlocked.CompareExchange(ref _firstClosedService, keyed, null);
                    return first is null ? resolved : SharedClosedService(typeof({{type.TypeName}}), first, resolved);
                }
                """);
        }
    }

    internal static void AppendMembers(StringBuilder b, CanonicalServiceDependency[] closed, bool blocked)
    {
        if (blocked)
        {
            for (var start = 0; start < closed.Length; start += BlockSize)
            {
                var block = start / BlockSize;
                b.AppendLine("private ServiceBlock_" + block + "? _serviceBlock_" + block + ";");
                b.AppendLine("private sealed class ServiceBlock_" + block + " {");
                for (var i = start; i < Math.Min(start + BlockSize, closed.Length); i++)
                    b.AppendLine("internal " + Storage(closed[i]) + "? _service_" + Id(closed[i]) + ";");
                b.AppendLine("}");
            }
        }
        else
        {
            foreach (var type in closed)
                b.AppendLine("private " + Storage(type) + "? _service_" + Id(type) + ";");
        }
        for (var i = 0; i < closed.Length; i++)
        {
            var type = closed[i];
            var id = Id(type);
            var storage = Storage(type);
            var slot = (blocked ? "block." : "") + "_service_" + id;
            b.AppendLine($"internal {storage} Required_{id}() =>");
            if (!blocked)
            {
                b.AppendLine($"global::System.Threading.Volatile.Read(ref {slot}) ?? ResolveRequired_{id}();");
                b.AppendLine($"private {storage} ResolveRequired_{id}() =>");
            }
            b.AppendLine($$"""
                Service_{{id}}()
                    ?? throw new global::System.InvalidOperationException("No service for type '" + typeof({{type.TypeName}}) + "' has been registered.");
                private {{storage}}? Service_{{id}}() {
                """);
            if (blocked)
                b.AppendLine("var block = global::System.Threading.Volatile.Read(ref _serviceBlock_" + i / BlockSize + ");");
            b.AppendLine("var cached = " + (blocked ? "block is null ? null : " : "") + "global::System.Threading.Volatile.Read(ref " + slot + ");");
            b.AppendLine($$"""
                    if (cached is not null) return cached;
                    // Native DI owns every activation and disposal; concurrent first calls share the published winner.
                    var resolved = ({{storage}}?)_services.GetService(typeof({{type.TypeName}}));
                """);
            if (blocked)
            {
                b.AppendLine($$"""
                    if (resolved is null) return null;
                    if (block is null) {
                        var created = new ServiceBlock_{{i / BlockSize}}();
                        block = global::System.Threading.Interlocked.CompareExchange(ref _serviceBlock_{{i / BlockSize}}, created, null) ?? created;
                    }
                    return global::System.Threading.Interlocked.CompareExchange(ref {{slot}}, resolved, null) ?? resolved;
                    """);
            }
            else
                b.AppendLine("return resolved is null ? null : global::System.Threading.Interlocked.CompareExchange(ref " + slot + ", resolved, null) ?? resolved;");
            b.AppendLine("}");
        }
    }

    private static string Storage(CanonicalServiceDependency type) => type.Boxed ? "object" : type.TypeName;
    private static string Id(CanonicalServiceDependency type) => CanonicalSplitSourceEmitter.Id(type.TypeName);
}
