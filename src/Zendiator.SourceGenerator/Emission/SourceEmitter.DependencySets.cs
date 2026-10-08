using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private const string ResolverType = "global::Zendiator.DependencyInjection.ZendiatorServiceResolver<Zendiator>";
    private const string InlineAttribute = "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";
    private const string NoInlineAttribute = "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]";

    private sealed class DependencySet(string name, List<EmissionType> types, int first, int firstValidation)
    {
        internal string Name { get; } = name;
        internal List<EmissionType> Types { get; } = types;
        internal int First { get; } = first;
        internal int FirstValidation { get; } = firstValidation;
        internal int IndexOf(EmissionType type) => Types.FindIndex(t => t.Name == type.Name);
    }

    private readonly record struct RouteServices(string Key, bool IsOpen, List<EmissionType> Types, EmissionType? First, EquatableArray<EmissionType> Validators);

    private static List<RouteServices> AllRouteServices(GenerationModel model)
    {
        var routes = new List<RouteServices>();
        void Add(string key, bool isOpen, IEnumerable<EmissionType> types, EquatableArray<EmissionType> validators = default) =>
            routes.Add(new RouteServices(key, isOpen, types.Concat(validators).GroupBy(t => t.Name, StringComparer.Ordinal)
                .Select(g => g.First()).ToList(), null, validators));
        void AddRoute(string key, EmissionRoute route) =>
            Add(key, route.IsOpen, route.Behaviors.Append(route.Handler), route.Validators);
        void AddMany(string prefix, EquatableArray<EmissionMultiRoute> multi)
        {
            for (var i = 0; i < multi.Count; i++)
                Add(prefix + i, multi[i].IsOpen || multi[i].Branches.Any(static b => b.HandlerIsOpen),
                    multi[i].Branches.SelectMany(static b => b.Behaviors.Append(b.Handler)));
        }
        var all = model.Routes;
        for (var i = 0; i < all.Requests.Single.Count; i++)
            AddRoute("Route" + i, all.Requests.Single[i]);
        for (var i = 0; i < all.Synchronous.Single.Count; i++)
            AddRoute("SyncRoute" + i, all.Synchronous.Single[i]);
        AddMany("MultiRoute", all.Requests.Multiple);
        AddMany("SyncMultiRoute", all.Synchronous.Multiple);
        for (var i = 0; i < all.Notifications.Count; i++)
            Add("NotificationRoute" + i, all.Notifications[i].IsOpen, all.Notifications[i].Subscribers.Select(static s => s.Handler));
        for (var i = 0; i < all.Streams.Count; i++)
            AddRoute("StreamRoute" + i, all.Streams[i]);
        // Each route resolves its first listed dependency right after creating its set.
        for (var i = 0; i < routes.Count; i++)
            routes[i] = routes[i] with { First = routes[i].Types.FirstOrDefault() };
        return routes;
    }

    // Open routes can close the same generic definitions at runtime, so those types keep slots.
    private static Dictionary<string, DependencySet> PlanDependencySets(GenerationModel model, bool singleService)
    {
        var sets = new Dictionary<string, DependencySet>(StringComparer.Ordinal);
        if (singleService) return sets;
        var routes = AllRouteServices(model);
        var uses = routes.SelectMany(static r => r.Types).GroupBy(static t => t.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal);
        var open = new HashSet<string>(routes.Where(static r => r.IsOpen).SelectMany(static r => r.Types)
            .SelectMany(static t => new[] { t.Name, t.OpenName }), StringComparer.Ordinal);
        foreach (var route in routes)
        {
            if (route.IsOpen) continue;
            var owned = route.Types.Where(t => t.IsReferenceType && !t.IsRefLikeType && uses[t.Name] == 1
                && !open.Contains(t.Name) && !open.Contains(t.OpenName)).ToList();
            if (owned.Count < 2) continue;
            var first = route.First is { } type ? owned.FindIndex(t => t.Name == type.Name) : -1;
            var firstValidator = route.Validators.FirstOrDefault(v => owned.Any(t => t.Name == v.Name));
            var firstValidation = firstValidator == null ? -1 : owned.FindIndex(t => t.Name == firstValidator.Name);
            sets.Add(route.Key, new DependencySet(route.Key + "Dependencies", owned, first, firstValidation));
        }
        return sets;
    }

    private DependencySet? SetOf(string routeKey) => _dependencySets.TryGetValue(routeKey, out var set) ? set : null;

    private static string Receiver(DependencySet? set, EmissionType type, string serviceName, string contract, bool direct, string services,
        string dependencies = "dependencies")
    {
        var index = set?.IndexOf(type) ?? -1;
        var expression = index >= 0 ? dependencies + ".Service" + index : services + ".GetDispatchService<" + serviceName + ">()";
        return direct ? expression : "((" + contract + ")" + expression + ")";
    }

    private static string NodeParameter(DependencySet? set, int node) =>
        set != null && node != 0 ? set.Name + " dependencies" : ResolverType + " services";

    private static string NodeArgument(DependencySet? set) => set != null ? "dependencies" : "services";

    private static string NodeServices(DependencySet? set, int node) => set != null && node != 0 ? "dependencies.Services" : "services";

    private static void EmitSetLookup(StringBuilder b, DependencySet? set, int node, string indent)
    {
        if (set != null && node == 0)
            b.AppendLine(indent + "var dependencies = " + set.Name + ".Get(services);");
    }

    private void EmitDependencySets(StringBuilder b)
    {
        foreach (var set in _dependencySets.Values)
        {
            b.AppendLine($$"""
                    private sealed class {{set.Name}}
                    {
                        internal readonly {{ResolverType}} Services;
                """);
            for (var i = 0; i < set.Types.Count; i++)
                b.AppendLine($$"""        private {{Name(set.Types[i])}}? _service{{i}};""");
            b.AppendLine($$"""
                        private {{set.Name}}({{ResolverType}} services) => Services = services;
                """);
            for (var i = 0; i < set.Types.Count; i++)
                b.AppendLine($$"""
                            internal {{Name(set.Types[i])}} Service{{i}}
                            {
                                {{InlineAttribute}}
                                get => global::System.Threading.Volatile.Read(ref _service{{i}}) ?? Services.ResolveDependency(ref _service{{i}});
                            }
                    """);
            if (set.FirstValidation >= 0)
                b.AppendLine($$"""
                            {{InlineAttribute}}
                            internal static {{set.Name}} GetForValidation({{ResolverType}} services) => services.GetDependencySet<{{set.Name}}>() ?? CreateForValidation(services);
                            {{NoInlineAttribute}}
                            private static {{set.Name}} CreateForValidation({{ResolverType}} services) => services.AddDependencySet(new {{set.Name}}(services), static dependencies => _ = dependencies.Service{{set.FirstValidation}});
                    """);
            var captureFirst = set.First >= 0 ? $", static dependencies => _ = dependencies.Service{set.First}" : "";
            b.AppendLine($$"""
                        {{InlineAttribute}}
                        internal static {{set.Name}} Get({{ResolverType}} services) => services.GetDependencySet<{{set.Name}}>() ?? Create(services);
                        {{NoInlineAttribute}}
                        private static {{set.Name}} Create({{ResolverType}} services) => services.AddDependencySet(new {{set.Name}}(services){{captureFirst}});
                        internal static {{set.Name}} Find({{ResolverType}} services) => services.GetDependencySet<{{set.Name}}>() ?? services.AddDependencySet(new {{set.Name}}(services));
                    }
                """);
        }
        if (!_model.Target.InheritServiceResolver || _dependencySets.Count == 0) return;
        b.AppendLine("""
                /// <inheritdoc />
                protected override object? GetGeneratedService(global::System.Type serviceType)
                {
            """);
        foreach (var set in _dependencySets.Values)
            for (var i = 0; i < set.Types.Count; i++)
                b.AppendLine($$"""        if (serviceType == typeof({{Name(set.Types[i])}})) return {{set.Name}}.Find(this).Service{{i}};""");
        b.AppendLine("""
                    return null;
                }
            """);
    }
}
