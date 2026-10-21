using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalStreamResolverConnection(string HintName, string Namespace, string ClassName, EquatableArray<CanonicalServiceDependency> Dependencies);
internal sealed record CanonicalServiceDependency(string TypeName, bool Open, bool Boxed);

// Route files own execution; the Mediator borrows one captured instance of each dependency.
internal static class CanonicalRouteServiceEmitter
{
    private static IEnumerable<MethodDeclarationSyntax> Entries(ClassDeclarationSyntax mediator) =>
        mediator.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));
    private static string Id(string ns, MethodDeclarationSyntax method) => CanonicalSplitSourceEmitter.Id(ns + "|" +
        method.Identifier.ValueText + "|" + (method.TypeParameterList?.Parameters.Count ?? 0) + "|" +
        method.ParameterList.Parameters[0].Type);
    private static string TypeKey(TypeSyntax type) => type.WithoutTrivia().NormalizeWhitespace().ToFullString();
    private static string TypeId(TypeSyntax type) => CanonicalSplitSourceEmitter.Id(TypeKey(type));
    private static bool Closed(TypeSyntax type) => !type.DescendantNodesAndSelf().OfType<OmittedTypeArgumentSyntax>().Any();
    private static bool ProviderField(MemberDeclarationSyntax member) => member is FieldDeclarationSyntax field &&
        field.Declaration.Type.ToString() == "global::System.IServiceProvider" &&
        field.Declaration.Variables.Count == 1 && field.Declaration.Variables[0].Identifier.ValueText == "_provider";
    private static TypeSyntax? ServiceType(InvocationExpressionSyntax call, string receiver)
    {
        if (call.Expression is not MemberAccessExpressionSyntax access ||
            access.Expression.ToString() != "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions" ||
            access.Name.Identifier.ValueText != "GetRequiredService" ||
            call.ArgumentList.Arguments.FirstOrDefault()?.Expression is not IdentifierNameSyntax provider ||
            provider.Identifier.ValueText != receiver) return null;
        return access.Name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count == 1 && call.ArgumentList.Arguments.Count == 1
            ? generic.TypeArgumentList.Arguments[0]
            : call.ArgumentList.Arguments.Count == 2 && call.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax of ? of.Type : null;
    }

    internal static CanonicalSourceFile Unsplit(CanonicalSourceFile file)
    {
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().Single();
        var mediator = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Zendiator");
        var registrar = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var fragment = Fragment(root, ns, mediator, registrar, file.BoxedDependencies);
        var b = CanonicalSplitSourceEmitter.Header(fragment.Externs);
        b.Append("namespace ").Append(fragment.Namespace).AppendLine(" {");
        foreach (var member in ns.Members.Where(m => m != mediator && m != registrar)) b.AppendLine(member.ToFullString());
        CanonicalSplitSourceEmitter.Mediator(b, fragment.Dependencies, Route(ns, mediator));
        b.AppendLine(fragment.Registrar).AppendLine("}");
        return CanonicalSplitSourceEmitter.File(file.HintName, b.ToString(), root, ns, mediator, fragment);
    }

    internal static IEnumerable<MemberDeclarationSyntax> Route(NamespaceDeclarationSyntax ns, ClassDeclarationSyntax mediator)
    {
        var suffix = Entries(mediator).Select(entry => Id(ns.Name.ToString(), entry)).Distinct(StringComparer.Ordinal).Single();
        var services = new ResolverCalls(Dependencies(ns).Where(Closed), route: mediator, suffix: "__" + suffix);
        foreach (var member in mediator.Members.Where(m => m is not ConstructorDeclarationSyntax && !ProviderField(m)))
            yield return (MemberDeclarationSyntax)services.Visit(member)!;
    }

    internal static EquatableArray<CanonicalStreamResolverConnection> StreamConnections(IEnumerable<CanonicalSourceFile> facades,
        IEnumerable<CanonicalStreamFallbackModel> candidates)
    {
        var connected = new HashSet<(string Namespace, string ClassName)>();
        foreach (var file in facades)
        {
            if (file.Api != null)
            {
                foreach (var helper in file.StreamHelpers) connected.Add((file.Api.Namespace, helper));
                continue;
            }
            var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
            foreach (var ns in root.Members.OfType<NamespaceDeclarationSyntax>())
            {
                var mediator = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Zendiator");
                foreach (var call in Entries(mediator).SelectMany(m => m.DescendantNodes().OfType<InvocationExpressionSyntax>()))
                    if (call.Expression is MemberAccessExpressionSyntax { Expression: SimpleNameSyntax helper, Name.Identifier.ValueText: "Create" } &&
                        call.ArgumentList.Arguments.FirstOrDefault()?.Expression is IdentifierNameSyntax { Identifier.ValueText: "_provider" })
                        connected.Add((ns.Name.ToString(), helper.Identifier.ValueText));
            }
        }
        var fragments = facades.Where(file => file.Composition != null).Select(file => file.Composition!).ToArray();
        return new(candidates.Where(c => connected.Contains((c.Namespace, c.ClassName)))
            .Select(c => new CanonicalStreamResolverConnection(c.HintName, c.Namespace, c.ClassName,
                new(fragments.Where(fragment => fragment.Namespace == c.Namespace).SelectMany(fragment => fragment.Dependencies)
                    .GroupBy(type => type.TypeName, StringComparer.Ordinal).Select(group => group.First() with { Boxed = group.Any(type => type.Boxed) }))))
            .OrderBy(c => c.HintName, StringComparer.Ordinal));
    }

    internal static CanonicalSourceFile Stream(CanonicalSourceFile file, EquatableArray<CanonicalStreamResolverConnection> connected)
    {
        var selected = connected.SingleOrDefault(c => c.HintName == file.HintName);
        if (selected == null) return file;
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var rewritten = new StreamServices(selected.Dependencies.Where(type => !type.Open).Select(type => SyntaxFactory.ParseTypeName(type.TypeName))).Visit(root)!;
        return file with { Source = rewritten.NormalizeWhitespace().ToFullString() };
    }

    private sealed class StreamServices(IEnumerable<TypeSyntax> closed) : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _closed = new(closed.Select(TypeKey), StringComparer.Ordinal);
        public override SyntaxNode? VisitParameter(ParameterSyntax node) =>
            node.Identifier.ValueText == "services" && node.Type?.ToString() == "global::System.IServiceProvider"
                ? node.WithType(SyntaxFactory.ParseTypeName("Zendiator").WithTriviaFrom(node.Type)) : base.VisitParameter(node);
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) =>
            node.Declaration.Type.ToString() == "global::System.IServiceProvider" &&
            node.Declaration.Variables.All(variable => variable.Identifier.ValueText == "_services")
                ? node.WithDeclaration(node.Declaration.WithType(SyntaxFactory.ParseTypeName("Zendiator").WithTriviaFrom(node.Declaration.Type)))
                : base.VisitFieldDeclaration(node);
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            foreach (var receiver in new[] { "services", "_services" })
            {
                if (ServiceType(node, receiver) is { } type && _closed.Contains(TypeKey(type)))
                    return SyntaxFactory.ParseExpression(receiver + ".Required_" + TypeId(type) + "()").WithTriviaFrom(node);
                if (node.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetService" } access &&
                    access.Expression.ToString() == receiver && node.ArgumentList.Arguments.Count == 1 &&
                    node.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax probe && probe.Type.ToString() == "global::System.IServiceProvider")
                    return SyntaxFactory.ParseExpression(receiver + ".ProbeStreamScope()").WithTriviaFrom(node);
            }
            return node;
        }
    }

    private static IEnumerable<TypeSyntax> Dependencies(NamespaceDeclarationSyntax ns)
    {
        var registrar = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        return registrar.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(n => n.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transient" } access &&
                access.Expression.ToString() == "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor" &&
                n.ArgumentList.Arguments.Count == 2 && n.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax &&
                n.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax)
            .Select(n => ((TypeOfExpressionSyntax)n.ArgumentList.Arguments[0].Expression).Type)
            .Where(t => t.ToString() != "global::" + ns.Name + ".Zendiator")
            .GroupBy(TypeKey, StringComparer.Ordinal).Select(g => g.First());
    }

    internal static CanonicalCompositionFragment Fragment(CompilationUnitSyntax root, NamespaceDeclarationSyntax ns,
        ClassDeclarationSyntax mediator, ClassDeclarationSyntax registrar, EquatableArray<string> boxedDependencies = default)
    {
        var dependencies = Dependencies(ns).ToArray();
        var boxed = new HashSet<string>(boxedDependencies.Select(type => TypeKey(SyntaxFactory.ParseTypeName(type))), StringComparer.Ordinal);
        // CanonicalServiceResolution preserves the bound value-type flag in the nongeneric lookup shape.
        foreach (var call in mediator.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (call.Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } && ServiceType(call, "_provider") is { } type)
                boxed.Add(TypeKey(type));
        return new(ns.Name.ToString(), registrar.ToFullString(), new(root.Externs.Select(e => e.WithLeadingTrivia().WithTrailingTrivia().ToString())),
            new(dependencies.Select(type => new CanonicalServiceDependency(TypeKey(type), !Closed(type), boxed.Contains(TypeKey(type))))));
    }

    internal static string Services(IEnumerable<CanonicalServiceDependency> dependencies)
    {
        var values = dependencies.ToArray();
        var closed = values.Where(type => !type.Open).ToArray();
        var open = values.Where(type => type.Open).ToArray();
        var b = new StringBuilder("""
            private readonly global::System.IServiceProvider _services;
            private Zendiator _provider => this;
            internal object? ProbeStreamScope() => _services.GetService(typeof(global::System.IServiceProvider));
            public Zendiator(global::System.IServiceProvider provider) {
                global::System.ArgumentNullException.ThrowIfNull(provider);
                _services = provider;
            }

            """);
        if (open.Length != 0)
        {
            b.AppendLine("private global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>? _genericServices;");
        }
        foreach (var type in closed)
            b.AppendLine("private " + (type.Boxed ? "object" : type.TypeName) + "? _service_" + CanonicalSplitSourceEmitter.Id(type.TypeName) + ";");
        foreach (var type in closed)
        {
            var id = CanonicalSplitSourceEmitter.Id(type.TypeName);
            var storage = type.Boxed ? "object" : type.TypeName;
            b.AppendLine($$"""
                internal {{storage}} Required_{{id}}() => Service_{{id}}()
                    ?? throw new global::System.InvalidOperationException("No service for type '" + typeof({{type.TypeName}}) + "' has been registered.");
                private {{storage}}? Service_{{id}}() {
                    var cached = global::System.Threading.Volatile.Read(ref _service_{{id}});
                    if (cached is not null) return cached;
                    // Native DI owns every activation and disposal; concurrent first calls share the published winner.
                    var resolved = ({{storage}}?)_services.GetService(typeof({{type.TypeName}}));
                    return resolved is null ? null : global::System.Threading.Interlocked.CompareExchange(ref _service_{{id}}, resolved, null) ?? resolved;
                }
                """);
        }
        b.AppendLine("object? global::System.IServiceProvider.GetService(global::System.Type serviceType) {");
        foreach (var type in closed)
            b.AppendLine("if (serviceType == typeof(" + type.TypeName + ")) return Service_" + CanonicalSplitSourceEmitter.Id(type.TypeName) + "();");
        if (open.Length != 0) b.AppendLine($$"""
            if (serviceType.IsConstructedGenericType && ({{string.Join(" || ", open.Select(t => "serviceType.GetGenericTypeDefinition() == typeof(" + t.TypeName + ")"))}})) {
                var slots = global::System.Threading.Volatile.Read(ref _genericServices);
                if (slots is not null && slots.TryGetValue(serviceType, out var cached)) return cached;
                var resolved = _services.GetService(serviceType);
                if (resolved is null) return null;
                if (slots is null) {
                    var created = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>();
                    slots = global::System.Threading.Interlocked.CompareExchange(ref _genericServices, created, null) ?? created;
                }
                return slots.GetOrAdd(serviceType, resolved);
            }
            """);
        b.AppendLine("return _services.GetService(serviceType); }");
        return b.ToString();
    }

    private sealed class ResolverCalls(IEnumerable<TypeSyntax> closed, string receiver = "_provider", ClassDeclarationSyntax? route = null, string suffix = "")
        : CanonicalMultiSourceEmitter.RouteNames(route, suffix)
    {
        private readonly HashSet<string> _closed = new(closed.Select(TypeKey), StringComparer.Ordinal);
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) => ProviderField(node)
            ? node.WithDeclaration(node.Declaration.WithType(SyntaxFactory.ParseTypeName("Zendiator").WithTriviaFrom(node.Declaration.Type)))
            : base.VisitFieldDeclaration(node);
        public override SyntaxNode? VisitParameter(ParameterSyntax node) => node.Identifier.ValueText == "provider" &&
            node.Parent?.Parent is ConstructorDeclarationSyntax { Parent: TypeDeclarationSyntax owner } && owner.Members.Any(ProviderField) &&
            node.Type?.ToString() == "global::System.IServiceProvider"
                ? node.WithType(SyntaxFactory.ParseTypeName("Zendiator").WithTriviaFrom(node.Type)) : base.VisitParameter(node);
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (ServiceType(node, receiver) is { } type && _closed.Contains(TypeKey(type)))
                return SyntaxFactory.ParseExpression(receiver + ".Required_" + TypeId(type) + "()").WithTriviaFrom(node);
            return node;
        }
    }
}
