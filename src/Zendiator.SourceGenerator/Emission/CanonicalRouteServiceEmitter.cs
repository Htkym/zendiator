using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zendiator.SourceGenerator;

// A DI-owned Route holds the scope-bound slots. User callbacks and opaque continuations stay intact.
internal static class CanonicalRouteServiceEmitter
{
    private static IEnumerable<MethodDeclarationSyntax> Entries(ClassDeclarationSyntax mediator) =>
        mediator.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));
    private static string Id(string ns, MethodDeclarationSyntax method) => CanonicalSplitSourceEmitter.Id(ns + "|" +
        method.Identifier.ValueText + "|" + (method.TypeParameterList?.Parameters.Count ?? 0) + "|" +
        method.ParameterList.Parameters[0].Type);
    private static string Name(string ns, MethodDeclarationSyntax method) => "Route_" + Id(ns, method);
    private static string Field(string ns, MethodDeclarationSyntax method) => "_route_" + Id(ns, method);

    internal static CanonicalSourceFile Unsplit(CanonicalSourceFile file)
    {
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().Single();
        var mediator = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Zendiator");
        var registrar = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var generated = Composition(ns, mediator, registrar);
        var joined = mediator.WithMembers(SyntaxFactory.List(generated.Shared.Members.Concat(Route(ns, mediator))));
        return file with { Source = root.ReplaceNode(ns, ns.WithMembers(SyntaxFactory.List(ns.Members.Select(m =>
            ReferenceEquals(m, mediator) ? joined : ReferenceEquals(m, registrar) ? generated.Registrar : m)))).NormalizeWhitespace().ToFullString() };
    }

    internal static IEnumerable<MemberDeclarationSyntax> Route(NamespaceDeclarationSyntax ns, ClassDeclarationSyntax mediator)
    {
        var entries = Entries(mediator).ToArray();
        var name = entries.Select(entry => Name(ns.Name.ToString(), entry)).Distinct(StringComparer.Ordinal).Single();
        foreach (var entry in entries)
        {
            var arguments = string.Join(", ", entry.ParameterList.Parameters.Select(p =>
                string.Concat(p.Modifiers.Where(t => t.IsKind(SyntaxKind.RefKeyword) || t.IsKind(SyntaxKind.OutKeyword) || t.IsKind(SyntaxKind.InKeyword)).Select(t => t.Text + " ")) + p.Identifier.Text));
            var generic = entry.TypeParameterList == null ? "" : "<" + string.Join(", ", entry.TypeParameterList.Parameters.Select(p => p.Identifier.Text)) + ">";
            yield return entry.WithBody(null).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression(
                Field(ns.Name.ToString(), entry) + "." + entry.Identifier.Text + generic + "(" + arguments + ")"))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        var dependencies = Dependencies(ns).ToArray();
        var closed = dependencies.Where(t => !t.DescendantNodesAndSelf().OfType<OmittedTypeArgumentSyntax>().Any()).ToArray();
        var open = dependencies.Except(closed).ToArray();
        var b = new StringBuilder("internal sealed class " + name + " : global::System.IServiceProvider, global::System.IDisposable {\n");
        b.AppendLine("private readonly global::System.IServiceProvider _services;");
        b.AppendLine("private readonly ResolutionPolicy _policy; private int _disposed;");
        for (var i = 0; i < closed.Length; i++) b.AppendLine("private object? _service" + i + "; private readonly bool _cache" + i + ";");
        if (open.Length != 0) b.AppendLine("private global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>? _genericServices;");
        var names = new OwnerTypeNames(name);
        foreach (var member in mediator.Members.Where(m => m is not ConstructorDeclarationSyntax))
            b.AppendLine(names.Visit(member)!.ToFullString());
        b.AppendLine("public " + name + "(global::System.IServiceProvider provider) {");
        b.AppendLine("global::System.ArgumentNullException.ThrowIfNull(provider); _services = provider; _provider = this;");
        b.AppendLine("_policy = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ResolutionPolicy>(provider);");
        for (var i = 0; i < closed.Length; i++) b.AppendLine("_cache" + i + " = _policy.Cacheable(typeof(" + closed[i] + ")); ");
        b.AppendLine("}");
        b.AppendLine("public void Dispose() => global::System.Threading.Volatile.Write(ref _disposed, 1);");
        b.AppendLine("public object? GetService(global::System.Type serviceType) {");
        b.AppendLine("if (global::System.Threading.Volatile.Read(ref _disposed) != 0) throw new global::System.ObjectDisposedException(\"IServiceProvider\");");
        for (var i = 0; i < closed.Length; i++) b.AppendLine($$"""
            if (serviceType == typeof({{closed[i]}})) {
                if (!_cache{{i}}) return _services.GetService(serviceType);
                var cached = global::System.Threading.Volatile.Read(ref _service{{i}});
                // Route.Dispose can be delayed behind other async disposables; keep the native scope check on hits.
                if (cached is not null) { _services.GetService(typeof(global::System.IServiceProvider)); return cached; }
                // Resolve through DI before publishing: its own lifetime synchronization and retry behavior remain authoritative.
                var resolved = _services.GetService(serviceType);
                return resolved is null ? null : global::System.Threading.Interlocked.CompareExchange(ref _service{{i}}, resolved, null) ?? resolved;
            }
            """);
        if (open.Length != 0) b.AppendLine($$"""
            if (serviceType.IsConstructedGenericType && ({{string.Join(" || ", open.Select(t => "serviceType.GetGenericTypeDefinition() == typeof(" + t + ")"))}}) && _policy.Cacheable(serviceType)) {
                var slots = global::System.Threading.Volatile.Read(ref _genericServices);
                if (slots is null) {
                    var created = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>();
                    slots = global::System.Threading.Interlocked.CompareExchange(ref _genericServices, created, null) ?? created;
                }
                if (slots.TryGetValue(serviceType, out var cached)) { _services.GetService(typeof(global::System.IServiceProvider)); return cached; }
                var resolved = _services.GetService(serviceType);
                return resolved is null ? null : slots.GetOrAdd(serviceType, resolved);
            }
            """);
        b.AppendLine("return _services.GetService(serviceType); } }");
        yield return SyntaxFactory.ParseMemberDeclaration(b.ToString())!;
    }

    private static IEnumerable<TypeSyntax> Dependencies(NamespaceDeclarationSyntax ns)
    {
        var registrar = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        return registrar.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(n => n.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Scoped" } access &&
                access.Expression.ToString() == "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor" &&
                n.ArgumentList.Arguments.Count == 2 && n.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax &&
                n.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax)
            .Select(n => ((TypeOfExpressionSyntax)n.ArgumentList.Arguments[0].Expression).Type)
            .Where(t => t.ToString() != "global::" + ns.Name + ".Zendiator")
            .GroupBy(t => t.ToString(), StringComparer.Ordinal).Select(g => g.First());
    }

    internal static (ClassDeclarationSyntax Shared, ClassDeclarationSyntax Registrar) Composition(
        NamespaceDeclarationSyntax ns, ClassDeclarationSyntax mediator, ClassDeclarationSyntax registrar)
    {
        var entries = Entries(mediator).GroupBy(entry => Name(ns.Name.ToString(), entry), StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var members = new List<MemberDeclarationSyntax>();
        foreach (var entry in entries) members.Add(SyntaxFactory.ParseMemberDeclaration("private readonly " + Name(ns.Name.ToString(), entry) + " " + Field(ns.Name.ToString(), entry) + ";")!);
        var constructor = new StringBuilder("public Zendiator(global::System.IServiceProvider provider) { global::System.ArgumentNullException.ThrowIfNull(provider);\n");
        foreach (var entry in entries) constructor.AppendLine(Field(ns.Name.ToString(), entry) + " = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<" + Name(ns.Name.ToString(), entry) + ">(provider);");
        members.Add(SyntaxFactory.ParseMemberDeclaration(constructor.Append("}").ToString())!);
        members.Add(SyntaxFactory.ParseMemberDeclaration("""
            internal sealed class ResolutionPolicy {
                private readonly global::System.Collections.Generic.Dictionary<global::System.Type, bool> _lifetimes;
                internal ResolutionPolicy(global::System.Collections.Generic.Dictionary<global::System.Type, bool> lifetimes) { _lifetimes = lifetimes; }
                internal bool Cacheable(global::System.Type type) => _lifetimes.TryGetValue(type, out var cached) ? cached :
                    type.IsConstructedGenericType && _lifetimes.TryGetValue(type.GetGenericTypeDefinition(), out cached) && cached;
                internal static global::System.Collections.Generic.Dictionary<global::System.Type, bool> Capture(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
                    var lifetimes = new global::System.Collections.Generic.Dictionary<global::System.Type, bool>();
                    foreach (var descriptor in services) {
                        if (descriptor.IsKeyedService) continue;
                        lifetimes[descriptor.ServiceType] = descriptor.Lifetime != global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient && descriptor.ImplementationFactory is null;
                    }
                    return lifetimes;
                }
            }
            """)!);
        var add = registrar.Members.OfType<MethodDeclarationSyntax>().Single(); var statements = add.Body!.Statements.ToList();
        var registration = new StringBuilder();
        foreach (var entry in entries)
        {
            var type = "global::" + ns.Name + ".Zendiator." + Name(ns.Name.ToString(), entry);
            registration.AppendLine("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(" + type + "), typeof(" + type + "))); ");
        }
        var policy = "global::" + ns.Name + ".Zendiator.ResolutionPolicy";
        registration.AppendLine("var lifetimes = " + policy + ".Capture(services);");
        registration.AppendLine("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(" + policy + "), provider => new " + policy + "(lifetimes))); ");
        var block = (BlockSyntax)SyntaxFactory.ParseStatement("{" + registration + "}");
        statements.InsertRange(statements.Count - 1, block.Statements);
        return (mediator.WithModifiers(mediator.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword))).WithMembers(SyntaxFactory.List(members)),
            registrar.ReplaceNode(add, add.WithBody(add.Body.WithStatements(SyntaxFactory.List(statements)))));
    }

    private sealed class OwnerTypeNames(string routeName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => node.Identifier.ValueText == "Zendiator" &&
            node.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax)
                ? node.WithIdentifier(SyntaxFactory.Identifier(node.Identifier.LeadingTrivia, routeName, node.Identifier.TrailingTrivia)) : base.VisitIdentifierName(node);
    }
}
