using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalStreamResolverConnection(string HintName, string Namespace, string ClassName, string HandlerName);

// Route files own execution; one private resolver owns the Mediator's borrowed service slots.
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

    internal static CanonicalSourceFile Unsplit(CanonicalSourceFile file)
    {
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().Single();
        var mediator = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Zendiator");
        var registrar = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar");
        var generated = Composition(ns, mediator, registrar);
        var joined = generated.Shared.WithMembers(SyntaxFactory.List(generated.Shared.Members.Concat(Route(ns, mediator))));
        return file with
        {
            Source = root.ReplaceNode(ns, ns.WithMembers(SyntaxFactory.List(ns.Members.Select(m =>
            ReferenceEquals(m, mediator) ? joined : ReferenceEquals(m, registrar) ? generated.Registrar : m)))).NormalizeWhitespace().ToFullString()
        };
    }

    internal static IEnumerable<MemberDeclarationSyntax> Route(NamespaceDeclarationSyntax ns, ClassDeclarationSyntax mediator)
    {
        var suffix = Entries(mediator).Select(entry => Id(ns.Name.ToString(), entry)).Distinct(StringComparer.Ordinal).Single();
        var names = new CanonicalMultiSourceEmitter.RouteNames(mediator, "__" + suffix);
        var services = new ResolverCalls(Dependencies(ns).Where(Closed));
        foreach (var member in mediator.Members.Where(m => m is not ConstructorDeclarationSyntax && !ProviderField(m)))
            yield return (MemberDeclarationSyntax)names.Visit(services.Visit(member))!;
    }

    internal static EquatableArray<CanonicalStreamResolverConnection> StreamConnections(IEnumerable<CanonicalSourceFile> facades,
        IEnumerable<CanonicalStreamFallbackModel> candidates)
    {
        var connected = new HashSet<(string Namespace, string ClassName)>();
        foreach (var file in facades)
        {
            var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
            foreach (var ns in root.Members.OfType<NamespaceDeclarationSyntax>())
            {
                var mediator = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Zendiator");
                foreach (var call in Entries(mediator).SelectMany(m => m.DescendantNodes().OfType<InvocationExpressionSyntax>()))
                    if (call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax helper, Name.Identifier.ValueText: "Create" } &&
                        call.ArgumentList.Arguments.FirstOrDefault()?.Expression is IdentifierNameSyntax { Identifier.ValueText: "_provider" })
                        connected.Add((ns.Name.ToString(), helper.Identifier.ValueText));
            }
        }
        return new(candidates.Where(c => c.Generic == null && !c.HandlerOwned && CanonicalStreamPublicConnection.PlainZeroStageShape(c) &&
            connected.Contains((c.Namespace, c.ClassName))).Select(c => new CanonicalStreamResolverConnection(c.HintName, c.Namespace, c.ClassName, c.Handler.HandlerName))
            .OrderBy(c => c.HintName, StringComparer.Ordinal));
    }

    internal static CanonicalSourceFile Stream(CanonicalSourceFile file, EquatableArray<CanonicalStreamResolverConnection> connected)
    {
        var selected = connected.SingleOrDefault(c => c.HintName == file.HintName);
        if (selected == null) return file;
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().Single(n => n.Name.ToString() == selected.Namespace);
        var helper = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == selected.ClassName);
        var typed = (ClassDeclarationSyntax)new ResolverCalls(new[] { SyntaxFactory.ParseTypeName(selected.HandlerName) }, "services").Visit(helper)!;
        var parameters = typed.DescendantNodes().OfType<ParameterSyntax>().Where(p =>
            p.Identifier.ValueText == "services" && p.Type?.ToString() == "global::System.IServiceProvider").ToArray();
        typed = typed.ReplaceNodes(parameters, (parameter, _) => parameter.WithType(SyntaxFactory.ParseTypeName("DependencyResolver").WithTriviaFrom(parameter.Type!)))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
        // The connected helper shares the Mediator's private resolver without an adapter or a second cursor type.
        var mediator = SyntaxFactory.ClassDeclaration("Zendiator").WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword), SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typed));
        return file with { Source = root.ReplaceNode(helper, mediator).NormalizeWhitespace().ToFullString() };
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
            .GroupBy(TypeKey, StringComparer.Ordinal).Select(g => g.First());
    }

    internal static (ClassDeclarationSyntax Shared, ClassDeclarationSyntax Registrar) Composition(
        NamespaceDeclarationSyntax ns, ClassDeclarationSyntax mediator, ClassDeclarationSyntax registrar)
    {
        var dependencies = Dependencies(ns).ToArray();
        var closed = dependencies.Where(Closed).ToArray();
        var members = new List<MemberDeclarationSyntax>
        {
            SyntaxFactory.ParseMemberDeclaration("private readonly DependencyResolver _provider;")!,
            SyntaxFactory.ParseMemberDeclaration("""
                public Zendiator(global::System.IServiceProvider provider) {
                    global::System.ArgumentNullException.ThrowIfNull(provider);
                    var policy = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ResolutionPolicy>(provider);
                    _provider = new DependencyResolver(provider, policy);
                }
                """)!,
            Resolver(dependencies),
            SyntaxFactory.ParseMemberDeclaration($$"""
                internal sealed class ResolutionPolicy {
                    private readonly global::System.Collections.Generic.Dictionary<global::System.Type, bool> _lifetimes;
                    {{string.Join("\n", closed.Select(type => "internal readonly bool Cache_" + TypeId(type) + ";"))}}
                    internal ResolutionPolicy(global::System.Collections.Generic.Dictionary<global::System.Type, bool> lifetimes) {
                        _lifetimes = lifetimes;
                        {{string.Join("\n", closed.Select(type => "Cache_" + TypeId(type) + " = Cacheable(typeof(" + type + "));"))}}
                    }
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
                """)!
        };
        var add = registrar.Members.OfType<MethodDeclarationSyntax>().Single();
        var statements = add.Body!.Statements.ToList();
        var policy = "global::" + ns.Name + ".Zendiator.ResolutionPolicy";
        var registration = "var lifetimes = " + policy + ".Capture(services);\n" +
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(" + policy + "), provider => new " + policy + "(lifetimes)));";
        var block = (BlockSyntax)SyntaxFactory.ParseStatement("{" + registration + "}");
        statements.InsertRange(statements.Count - 1, block.Statements);
        return (mediator.WithModifiers(mediator.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword))).WithMembers(SyntaxFactory.List(members)),
            registrar.ReplaceNode(add, add.WithBody(add.Body.WithStatements(SyntaxFactory.List(statements)))));
    }

    private static MemberDeclarationSyntax Resolver(TypeSyntax[] dependencies)
    {
        var closed = dependencies.Where(Closed).ToArray();
        var open = dependencies.Where(t => !Closed(t)).ToArray();
        var b = new StringBuilder("private sealed class DependencyResolver : global::System.IServiceProvider {\nprivate readonly global::System.IServiceProvider _services;\n");
        if (open.Length != 0)
        {
            b.AppendLine("private readonly ResolutionPolicy _policy;");
            b.AppendLine("private global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>? _genericServices;");
        }
        foreach (var type in closed)
            b.AppendLine("private object? _service_" + TypeId(type) + "; private readonly bool _cache_" + TypeId(type) + ";");
        b.AppendLine("internal DependencyResolver(global::System.IServiceProvider services, ResolutionPolicy policy) { _services = services;");
        if (open.Length != 0) b.AppendLine("_policy = policy;");
        foreach (var type in closed)
            b.AppendLine("_cache_" + TypeId(type) + " = policy.Cache_" + TypeId(type) + ";");
        b.AppendLine("}");
        foreach (var type in closed)
        {
            var id = TypeId(type);
            b.AppendLine($$"""
                internal object Required_{{id}}() => Service_{{id}}()
                    ?? throw new global::System.InvalidOperationException("No service for type '" + typeof({{type}}) + "' has been registered.");
                private object? Service_{{id}}() {
                    if (!_cache_{{id}}) return _services.GetService(typeof({{type}}));
                    var cached = global::System.Threading.Volatile.Read(ref _service_{{id}});
                    if (cached is not null) { _services.GetService(typeof(global::System.IServiceProvider)); return cached; }
                    // Native DI owns activation, synchronization, retry and disposal; slots only borrow its result.
                    var resolved = _services.GetService(typeof({{type}}));
                    return resolved is null ? null : global::System.Threading.Interlocked.CompareExchange(ref _service_{{id}}, resolved, null) ?? resolved;
                }
                """);
        }
        b.AppendLine("public object? GetService(global::System.Type serviceType) {");
        foreach (var type in closed)
            b.AppendLine("if (serviceType == typeof(" + type + ")) return Service_" + TypeId(type) + "();");
        if (open.Length != 0) b.AppendLine($$"""
            if (serviceType.IsConstructedGenericType && ({{string.Join(" || ", open.Select(t => "serviceType.GetGenericTypeDefinition() == typeof(" + t + ")"))}}) && _policy.Cacheable(serviceType)) {
                var slots = global::System.Threading.Volatile.Read(ref _genericServices);
                if (slots is not null && slots.TryGetValue(serviceType, out var cached)) { _services.GetService(typeof(global::System.IServiceProvider)); return cached; }
                var resolved = _services.GetService(serviceType);
                if (resolved is null) return null;
                if (slots is null) {
                    var created = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>();
                    slots = global::System.Threading.Interlocked.CompareExchange(ref _genericServices, created, null) ?? created;
                }
                return slots.GetOrAdd(serviceType, resolved);
            }
            """);
        b.AppendLine("return _services.GetService(serviceType); } }");
        return SyntaxFactory.ParseMemberDeclaration(b.ToString())!;
    }

    private sealed class ResolverCalls(IEnumerable<TypeSyntax> closed, string receiver = "_provider") : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _closed = new(closed.Select(TypeKey), StringComparer.Ordinal);
        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) => ProviderField(node)
            ? node.WithDeclaration(node.Declaration.WithType(SyntaxFactory.ParseTypeName("DependencyResolver").WithTriviaFrom(node.Declaration.Type)))
            : base.VisitFieldDeclaration(node);
        public override SyntaxNode? VisitParameter(ParameterSyntax node) => node.Identifier.ValueText == "provider" &&
            node.Parent?.Parent is ConstructorDeclarationSyntax { Parent: TypeDeclarationSyntax owner } && owner.Members.Any(ProviderField) &&
            node.Type?.ToString() == "global::System.IServiceProvider"
                ? node.WithType(SyntaxFactory.ParseTypeName("DependencyResolver").WithTriviaFrom(node.Type)) : base.VisitParameter(node);
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is MemberAccessExpressionSyntax access &&
                access.Expression.ToString() == "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions" &&
                access.Name.Identifier.ValueText == "GetRequiredService" &&
                node.ArgumentList.Arguments.FirstOrDefault()?.Expression is IdentifierNameSyntax provider && provider.Identifier.ValueText == receiver)
            {
                TypeSyntax? type = access.Name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count == 1 && node.ArgumentList.Arguments.Count == 1
                    ? generic.TypeArgumentList.Arguments[0]
                    : node.ArgumentList.Arguments.Count == 2 && node.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax of ? of.Type : null;
                if (type != null && _closed.Contains(TypeKey(type)))
                {
                    ExpressionSyntax call = SyntaxFactory.ParseExpression(receiver + ".Required_" + TypeId(type) + "()");
                    if (access.Name is GenericNameSyntax) call = SyntaxFactory.CastExpression(type.WithoutTrivia(), call);
                    return call.WithTriviaFrom(node);
                }
            }
            return base.VisitInvocationExpression(node);
        }
    }
}
