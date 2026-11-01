namespace Zendiator.SourceGenerator;

internal static class CanonicalGenericServiceCacheEmitter
{
    internal static string Members() => """
        private object? _genericServices;
        private sealed class GenericServiceEntry(global::System.Type key, object value, GenericServiceEntry? next) {
            internal readonly global::System.Type Key = key;
            internal readonly object Value = value;
            internal readonly GenericServiceEntry? Next = next;
        }
        private object? GenericService(global::System.Type serviceType) {
            var snapshot = global::System.Threading.Volatile.Read(ref _genericServices);
            if (snapshot is global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object> dictionary) {
                if (dictionary.TryGetValue(serviceType, out var cached)) return cached;
            } else {
                for (var entry = (GenericServiceEntry?)snapshot; entry is not null; entry = entry.Next)
                    if (entry.Key == serviceType) return entry.Value;
            }
            var resolved = _services.GetService(serviceType);
            if (resolved is null) return null;
            while (true) {
                snapshot = global::System.Threading.Volatile.Read(ref _genericServices);
                if (snapshot is global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object> slots)
                    return slots.GetOrAdd(serviceType, resolved);
                var count = 0;
                for (var entry = (GenericServiceEntry?)snapshot; entry is not null; entry = entry.Next) {
                    if (entry.Key == serviceType) return entry.Value;
                    count++;
                }
                object updated;
                if (count < 4) {
                    updated = new GenericServiceEntry(serviceType, resolved, (GenericServiceEntry?)snapshot);
                } else {
                    var promoted = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>();
                    for (var entry = (GenericServiceEntry?)snapshot; entry is not null; entry = entry.Next)
                        promoted.TryAdd(entry.Key, entry.Value);
                    promoted.TryAdd(serviceType, resolved);
                    updated = promoted;
                }
                if (global::System.Threading.Interlocked.CompareExchange(ref _genericServices, updated, snapshot) == snapshot)
                    return resolved;
            }
        }
        """;
}
