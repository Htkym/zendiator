using System;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalClosedServiceCacheEmitter
{
    private const int BlockSize = 16;

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
            b.AppendLine($$"""
                internal {{storage}} Required_{{id}}() => Service_{{id}}()
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
