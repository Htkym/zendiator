using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zendiator.SourceGenerator;

internal static class CanonicalMediatorIdentity
{
    // Read the existing registrar's actual DI registrations, not generated names alone.
    // This bounded source proof deliberately does not infer metadata-only registrations.
    internal static ImmutableHashSet<ITypeSymbol> Discover(Compilation compilation, CancellationToken cancellationToken)
    {
        var result = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);
        var configuration = compilation.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfiguration");
        var snapshot = configuration?.ContainingAssembly.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot");
        var collection = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
        var descriptor = collection?.ContainingAssembly.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceDescriptor");
        var extensions = collection?.ContainingAssembly.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions");
        var providerExtensions = collection?.ContainingAssembly.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions");
        if (snapshot is null || collection is null || descriptor is null || extensions is null || providerExtensions is null)
            return result.ToImmutable();

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = tree.GetRoot(cancellationToken);
            foreach (var declaration in root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText == "ZendiatorGeneratedRegistrar"))
            {
                var model = compilation.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol { IsStatic: true } registrar) continue;
                var space = registrar.ContainingNamespace;
                if (!IsCompositionNamespace(space, compilation)) continue;
                var implementations = space.GetTypeMembers("Zendiator").Where(t => t.Arity == 0 && t.IsSealed && t.TypeKind == TypeKind.Class).ToArray();
                var contracts = space.GetTypeMembers("IZendiator").Where(t => t.Arity == 0 && t.TypeKind == TypeKind.Interface).ToArray();
                if (implementations.Length != 1 || contracts.Length != 1) continue;
                var implementation = implementations[0];
                var contract = contracts[0];
                if (!implementation.Interfaces.Any(i => Same(i, contract))) continue;

                foreach (var methodSyntax in declaration.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(methodSyntax, cancellationToken) is not IMethodSymbol { IsStatic: true, Name: "Add", Parameters.Length: 2 } method ||
                        !Same(method.ReturnType, collection) || !Same(method.Parameters[0].Type, collection) || !Same(method.Parameters[1].Type, snapshot)) continue;
                    var registersImplementation = false;
                    var registersContract = false;
                    var registersAlias = false;
                    foreach (var call in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol { Name: "TryAdd" } add ||
                            !Same(add.ContainingType, extensions) || call.ArgumentList.Arguments.Count != 2 ||
                            !Same(model.GetSymbolInfo(call.ArgumentList.Arguments[0].Expression, cancellationToken).Symbol, method.Parameters[0]) ||
                            call.ArgumentList.Arguments[1].Expression is not InvocationExpressionSyntax scoped ||
                            model.GetSymbolInfo(scoped, cancellationToken).Symbol is not IMethodSymbol { Name: "Scoped" } factory ||
                            !Same(factory.ContainingType, descriptor) || scoped.ArgumentList.Arguments.Count != 2 ||
                            scoped.ArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax service) continue;
                        var serviceType = model.GetTypeInfo(service.Type, cancellationToken).Type;
                        var value = scoped.ArgumentList.Arguments[1].Expression;
                        if (Same(serviceType, implementation) && value is TypeOfExpressionSyntax concrete &&
                            Same(model.GetTypeInfo(concrete.Type, cancellationToken).Type, implementation)) registersImplementation = true;
                        if (Same(serviceType, contract) && value is TypeOfExpressionSyntax target &&
                            Same(model.GetTypeInfo(target.Type, cancellationToken).Type, implementation)) registersContract = true;
                        if (Same(serviceType, contract) && value is SimpleLambdaExpressionSyntax { Body: InvocationExpressionSyntax resolution } lambda &&
                            model.GetSymbolInfo(resolution, cancellationToken).Symbol is IMethodSymbol { Name: "GetRequiredService", TypeArguments.Length: 1 } resolve &&
                            Same(resolve.ContainingType, providerExtensions) && Same(resolve.TypeArguments[0], implementation) &&
                            resolution.ArgumentList.Arguments.Count == 1 &&
                            Same(model.GetSymbolInfo(resolution.ArgumentList.Arguments[0].Expression, cancellationToken).Symbol,
                                model.GetDeclaredSymbol(lambda.Parameter, cancellationToken))) registersAlias = true;
                    }
                    // Earlier compositions used a concrete registration plus a contract alias.
                    if (registersContract || registersImplementation && registersAlias)
                    {
                        result.Add(implementation);
                        result.Add(contract);
                    }
                }
            }
        }
        return result.ToImmutable();
    }

    private static bool Same(ISymbol? left, ISymbol? right) =>
        left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);

    private static bool IsCompositionNamespace(INamespaceSymbol space, Compilation compilation)
    {
        if (space.IsGlobalNamespace || space.ContainingNamespace.Name != "Generated") return false;
        var parent = space.ContainingNamespace.ContainingNamespace;
        var globalDefault = parent.Name == "Zendiator" && parent.ContainingNamespace.IsGlobalNamespace;
        if (globalDefault && space.Name == "Default") return true;
        var prefix = parent.ToDisplayString(new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
        var marker = compilation.GetTypeByMetadataName(prefix + "." + space.Name);
        if (marker is null && globalDefault) marker = compilation.GetTypeByMetadataName(space.Name);
        if (marker is not { TypeKind: TypeKind.Class, IsSealed: true, IsStatic: false, Arity: 0, ContainingType: null, DeclaredAccessibility: Accessibility.Public }) return false;
        var attribute = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly
            .GetTypeByMetadataName("Zendiator.ZendiatorCompositionAttribute");
        return attribute is not null && marker.GetAttributes().Count(a => Same(a.AttributeClass, attribute)) == 1;
    }
}
