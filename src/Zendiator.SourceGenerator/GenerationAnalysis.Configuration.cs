using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GeneratedNamespace;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private bool assemblyMode;
    private string? assemblyNamespace;
    private INamedTypeSymbol? mediator;
    private bool diMode;
    private string? diNamespace;
    private readonly HashSet<IAssemblySymbol> diAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
    private readonly List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)> diPipelines = new List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)>();
    private readonly List<INamedTypeSymbol> diNotifications = new List<INamedTypeSymbol>();
    private readonly List<INamedTypeSymbol> diOpenNotifications = new List<INamedTypeSymbol>();
    private readonly Dictionary<string, (int Order, Location Location)> diHandlerOrders = new Dictionary<string, (int Order, Location Location)>(StringComparer.Ordinal);
    private readonly List<(int Version, string Data, bool IsLambda, bool IsExtensionForm)> diCallSites = new List<(int Version, string Data, bool IsLambda, bool IsExtensionForm)>();
    private string diFingerprint = null!;

    private static string Position(Location location)
    {
        var span = location.GetLineSpan();
        return span.Path + "(" + (span.StartLinePosition.Line + 1) + "," + (span.StartLinePosition.Character + 1) + ")";
    }

    private GenerationResult? Configure()
    {
        var assemblyGenAttributes = compilation.Assembly.GetAttributes()
            .Where(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.GenerateZendiatorAttribute").ToArray();
        if (assemblyGenAttributes.Length > 1)
        {
            Error(6, "Only one assembly-level [GenerateZendiator] attribute is allowed per compilation.");
            return new GenerationResult("", "", errors);
        }
        var assemblyGen = assemblyGenAttributes.FirstOrDefault();
        var boundDiCalls = new List<(InvocationExpressionSyntax Invocation, LambdaExpressionSyntax? Lambda, bool IsExtensionForm)>();
        var hasBareDiCall = false;
        var hasLambdaDiCall = false;
        Location firstBareLocation = Location.None;
        Location firstLambdaLocation = Location.None;
        var diConfigType = compilation.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfiguration");
        var diConfigAssembly = diConfigType?.ContainingAssembly;
        if (diConfigAssembly != null)
        {
            foreach (var invocation in diInvocations)
            {
                ct.ThrowIfCancellationRequested();
                var model = compilation.GetSemanticModel(invocation.SyntaxTree);
                var info = model.GetSymbolInfo(invocation, ct);
                if (info.Symbol is not IMethodSymbol method)
                {
                    // A DI-shaped call that failed to bind still deserves guidance
                    // when every candidate is the package bootstrap.
                    var candidates = info.CandidateSymbols.OfType<IMethodSymbol>()
                        .Select(static m => m.OriginalDefinition).ToList();
                    if (candidates.Count != 0 && candidates.All(m =>
                        m.ContainingType?.Name == "ZendiatorServiceCollectionExtensions" &&
                        m.ContainingType.ContainingNamespace?.ToDisplayString() == "Zendiator.DependencyInjection" &&
                        Same(m.ContainingAssembly, diConfigAssembly)))
                    {
                        hasLambdaDiCall = true;
                        if (firstLambdaLocation == Location.None) firstLambdaLocation = invocation.GetLocation();
                        ErrorAt(17, "AddZendiator call does not match a supported configuration shape. Fix the lambda and its statements, then use direct recorder calls with constant values.", invocation.GetLocation());
                    }
                    continue;
                }
                var original = method.OriginalDefinition;
                if (original.ContainingType?.Name != "ZendiatorServiceCollectionExtensions") continue;
                if (original.ContainingType.ContainingNamespace?.ToDisplayString() != "Zendiator.DependencyInjection") continue;
                if (!Same(original.ContainingAssembly, diConfigAssembly)) continue;
                // Shape is read from the bound (possibly reduced) method: extension
                // invocations drop `this`, explicit static calls keep it.
                var unreduced = original.Parameters;
                if (unreduced.Length < 1 || unreduced.Length > 2) continue;
                if (unreduced.Length == 2 &&
                    (unreduced[1].Type is not INamedTypeSymbol unbound ||
                     unbound.OriginalDefinition.ToDisplayString() != "System.Action<T>" ||
                     unbound.TypeArguments.Length != 1 ||
                     !Same(unbound.TypeArguments[0].OriginalDefinition, diConfigType!.OriginalDefinition)))
                    continue;
                var parameters = method.Parameters;
                var last = parameters.Length == 0 ? null : parameters[parameters.Length - 1];
                var isLambdaForm = last?.Type is INamedTypeSymbol action &&
                    action.OriginalDefinition.ToDisplayString() == "System.Action<T>" &&
                    action.TypeArguments.Length == 1 &&
                    Same(action.TypeArguments[0].OriginalDefinition, diConfigType!.OriginalDefinition);
                if (!isLambdaForm)
                {
                    hasBareDiCall = true;
                    if (firstBareLocation == Location.None) firstBareLocation = invocation.GetLocation();
                    boundDiCalls.Add((invocation, null, method.ReducedFrom != null));
                    continue;
                }
                var lambdaArg = invocation.ArgumentList?.Arguments.LastOrDefault();
                if (lambdaArg?.Expression is LiteralExpressionSyntax literal &&
                    literal.RawKind == (int)SyntaxKind.NullLiteralExpression)
                {
                    hasLambdaDiCall = true;
                    if (firstLambdaLocation == Location.None) firstLambdaLocation = invocation.GetLocation();
                    ErrorAt(17, "AddZendiator configuration does not accept null. Omit the argument for the default configuration or pass a lambda.", lambdaArg.GetLocation());
                    continue;
                }
                hasLambdaDiCall = true;
                if (firstLambdaLocation == Location.None) firstLambdaLocation = invocation.GetLocation();
                if (lambdaArg?.Expression is not LambdaExpressionSyntax lambda)
                {
                    ErrorAt(17, "AddZendiator configuration must be a lambda with supported calls. Write static configuration => { ... } with direct recorder calls.", lambdaArg?.GetLocation() ?? invocation.GetLocation());
                    continue;
                }
                boundDiCalls.Add((invocation, lambda, method.ReducedFrom != null));
            }
        }
        if (declarations.Length == 0 && assemblyGen == null && !hasBareDiCall && !hasLambdaDiCall) return new GenerationResult("", "", errors);
        if (declarations.Length != 0 && assemblyGen != null)
        {
            Error(6, "Cannot combine class-level [GenerateZendiator] with assembly-level [GenerateZendiator]. Keep one configuration mode.", assemblyGen.AttributeClass);
            return new GenerationResult("", "", errors);
        }
        if (declarations.Length > 1)
        {
            Error(5, "Exactly one [GenerateZendiator] declaration is allowed per compilation.");
            return new GenerationResult("", "", errors);
        }
        assemblyMode = assemblyGen != null;


        assemblyNamespace = null;

        mediator = null;

        if (assemblyMode)
        {
            assemblyNamespace = ResolveAssemblyNamespace(assemblyGen!, compilation, errors);
            if (assemblyNamespace == null) return new GenerationResult("", "", errors);
        }
        else if (declarations.Length != 0)
        {
            mediator = declarations[0];
            var classGenAttr = mediator.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.GenerateZendiatorAttribute");
            if (classGenAttr != null && classGenAttr.NamedArguments.Any(static p => p.Key == "Namespace" && p.Value.Value is string s && s.Length != 0))
            {
                Error(5, "Namespace is only valid on assembly-level [GenerateZendiator].", mediator);
                return new GenerationResult("", "", errors);
            }
            var syntax = mediator.DeclaringSyntaxReferences.Select(r => r.GetSyntax(ct)).OfType<ClassDeclarationSyntax>().ToArray();
            if (mediator.Name != "Zendiator" || mediator.Arity != 0 || mediator.ContainingType != null ||
                mediator.DeclaredAccessibility != Accessibility.Public || !mediator.IsSealed || mediator.IsStatic ||
                syntax.Length == 0 || syntax.Any(s => !s.Modifiers.Any(SyntaxKind.PartialKeyword) || s.ParameterList != null) ||
                mediator.BaseType?.SpecialType != SpecialType.System_Object || mediator.Interfaces.Length != 0 ||
                mediator.GetMembers().Any(m => !m.IsImplicitlyDeclared) ||
                mediator.ContainingNamespace.GetTypeMembers("IZendiator").Length != 0 ||
                mediator.ContainingNamespace.GetTypeMembers("ZendiatorServiceCollectionExtensions").Length != 0)
            {
                Error(5, "Declare an empty, top-level public sealed partial class Zendiator; IZendiator and ZendiatorServiceCollectionExtensions are reserved.", mediator);
                return new GenerationResult("", "", errors);
            }
        }
        var diSettings = new List<DiSetting>();
        var hasAttributeConfig = declarations.Length != 0 || assemblyGen != null;
        if (!(hasAttributeConfig && hasLambdaDiCall))
        {
            foreach (var (invocation, lambda, _) in boundDiCalls)
            {
                ct.ThrowIfCancellationRequested();
                if (lambda == null) continue;
                var setting = AnalyzeDiLambda(invocation, lambda, compilation.GetSemanticModel(invocation.SyntaxTree));
                if (setting != null) diSettings.Add(setting);
            }
        }
        if (hasAttributeConfig && hasLambdaDiCall)
        {
            ErrorAt(15, "Conflicting configuration sources: attribute-based configuration and AddZendiator configuration lambdas cannot be combined in one compilation. Keep one source.", firstLambdaLocation);
        }
        else if (!hasAttributeConfig && hasBareDiCall)
        {
            diSettings.Add(new DiSetting { CallLocation = firstBareLocation });
        }
        if (!hasAttributeConfig && diSettings.Count != 0)
        {
            var groups = diSettings.GroupBy(static setting => setting.StructureKey()).ToList();
            if (groups.Count != 1)
            {
                var first = groups[0].First().CallLocation;
                foreach (var group in groups.Skip(1))
                {
                    foreach (var setting in group)
                        ErrorAt(16, $"Multiple AddZendiator configurations with different structures. First structure at {Position(first)}. Merge them into one configuration.", setting.CallLocation);
                }
            }
        }

        // DI backend inputs. Built from the single analyzed structure; the shared
        // discovery, route, and emit machinery below runs unchanged afterwards.
        diMode = !hasAttributeConfig && (hasBareDiCall || hasLambdaDiCall);

        diNamespace = null;

        diFingerprint = "";

        if (diMode)
        {
            foreach (var (invocation, lambda, isExtensionForm) in boundDiCalls)
            {
                ct.ThrowIfCancellationRequested();
                var location = compilation.GetSemanticModel(invocation.SyntaxTree).GetInterceptableLocation(invocation, ct);
                if (location == null)
                {
                    ErrorAt(20, "AddZendiator call cannot be connected to generated registration. Keep the call inside an ordinary method body.", invocation.GetLocation());
                    continue;
                }
                diCallSites.Add((location.Version, location.Data, lambda != null, isExtensionForm));
            }
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name is "Zendiator.IncludeAssemblyAttribute" or "Zendiator.PipelineBehaviorAttribute" or "Zendiator.NotificationAttribute")
                {
                    ErrorAt(15, $"Conflicting configuration sources: assembly-level {name} cannot be combined with AddZendiator configuration. Move the setting into the lambda.", attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None);
                }
            }
            var representative = diSettings.OrderBy(static setting => setting.StructureKey(), StringComparer.Ordinal).FirstOrDefault();
            if (representative != null) diFingerprint = representative.StructureKey();
            if (representative != null)
            {
                foreach (var entry in representative.Markers)
                    diAssemblies.Add(entry.Type.ContainingAssembly);
                foreach (var identity in representative.Assemblies)
                {
                    var found = compilation.Assembly.Identity.ToString() == identity
                        ? compilation.Assembly
                        : compilation.SourceModule.ReferencedAssemblySymbols.FirstOrDefault(a => a.Identity.ToString() == identity);
                    if (found == null)
                    {
                        ErrorAt(18, $"Unknown assembly {identity}.", representative.CallLocation);
                        continue;
                    }
                    diAssemblies.Add(found);
                }
                foreach (var entry in representative.Behaviors)
                    diPipelines.Add((entry.Type, entry.Order, entry.IsOpen));
                foreach (var entry in representative.Notifications)
                {
                    var notification = entry.Type;
                    var name = entry.Key;
                    if (notification.IsRefLikeType)
                    {
                        ErrorAt(12, $"Notification {name} is a ref struct. Ref struct notifications are not supported.", representative.CallLocation);
                        continue;
                    }
                    var notificationMarker = compilation.GetTypeByMetadataName("Zendiator.INotification");
                    if (notificationMarker == null || !notification.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationMarker)))
                    {
                        ErrorAt(13, $"Notification {name} must implement Zendiator.INotification.", representative.CallLocation);
                        continue;
                    }
                    if (!Public(notification))
                    {
                        ErrorAt(13, $"Notification {name} must be public.", representative.CallLocation);
                        continue;
                    }
                    if (IsOpenDefinition(notification)) diOpenNotifications.Add(notification);
                    else diNotifications.Add(notification);
                }
                foreach (var entry in representative.HandlerOrders)
                {
                    var attribute = entry.Type.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.HandlerOrderAttribute");
                    if (attribute != null)
                    {
                        var attributeOrder = AttributeOrder(attribute);
                        if (attributeOrder != entry.Order)
                        {
                            var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None;
                            ErrorAt(18, $"Handler {entry.Key} has conflicting orders: configuration specifies {entry.Order} but [HandlerOrder] specifies {attributeOrder} at {Position(attributeLocation)}. Keep one value.", representative.CallLocation);
                            continue;
                        }
                    }
                    diHandlerOrders[entry.Key] = (entry.Order, representative.CallLocation);
                }
                var requested = representative.Namespace;
                var resolved = requested is { Length: > 0 } && !string.IsNullOrWhiteSpace(requested)
                    ? requested.Trim()
                    : DefaultGeneratedNamespace(compilation.AssemblyName);
                if (resolved == null || !IsValidNamespace(resolved))
                {
                    ErrorAt(18, $"Invalid generation namespace '{requested ?? "<empty>"}'. Use dot-separated identifiers.", representative.CallLocation);
                }
                else
                {
                    foreach (var reserved in new[] { "Zendiator", "IZendiator", "ZendiatorServiceCollectionExtensions", "ZendiatorGeneratedRegistrar" })
                    {
                        if (compilation.GetTypeByMetadataName(resolved + "." + reserved) != null)
                        {
                            ErrorAt(7, $"Generated name collision: '{resolved}.{reserved}' already exists. Choose a different Namespace.", representative.CallLocation);
                            resolved = null;
                            break;
                        }
                    }
                    diNamespace = resolved;
                }
            }
            else
            {
                diNamespace = DefaultGeneratedNamespace(compilation.AssemblyName);
                if (diNamespace == null || !IsValidNamespace(diNamespace)) diNamespace = null;
            }
            if (diNamespace == null && errors.Count == 0)
            {
                ErrorAt(7, "Unable to determine a generation namespace. Set configuration.Namespace explicitly.", Location.None);
            }
        }
        return null;
    }
}
