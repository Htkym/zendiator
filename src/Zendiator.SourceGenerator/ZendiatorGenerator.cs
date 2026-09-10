using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Zendiator.SourceGenerator;

/// <summary>Generates concrete request overloads, continuations and DI registration.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ZendiatorGenerator : IIncrementalGenerator
{
    private const string Request = "Zendiator.IRequest`1";
    private const string Handler = "Zendiator.IRequestHandler`2";
    private const string Behavior = "Zendiator.IPipelineBehavior`2";
    private const string VoidRequest = "Zendiator.IRequest";
    private const string VoidHandler = "Zendiator.IRequestHandler`1";
    private const string VoidBehavior = "Zendiator.IPipelineBehavior`1";
    private const string Notification = "Zendiator.INotification";
    private const string NotificationHandler = "Zendiator.INotificationHandler`1";
    private const string MultiResponse = "Zendiator.IMultiRequest`1";
    private const string MultiVoid = "Zendiator.IMultiRequest";
    private const string SyncRequest = "Zendiator.ISyncRequest`1";
    private const string SyncVoidRequest = "Zendiator.ISyncRequest";
    private const string SyncHandler = "Zendiator.ISyncRequestHandler`2";
    private const string SyncVoidHandler = "Zendiator.ISyncRequestHandler`1";
    private const string SyncBehavior = "Zendiator.ISyncPipelineBehavior`2";
    private const string SyncVoidBehavior = "Zendiator.ISyncPipelineBehavior`1";
    private const string SyncMultiResponse = "Zendiator.ISyncMultiRequest`1";
    private const string SyncMultiVoid = "Zendiator.ISyncMultiRequest";
    private const string StreamRequest = "Zendiator.IStreamRequest`1";
    private const string StreamHandler = "Zendiator.IStreamRequestHandler`2";
    private const string StreamBehavior = "Zendiator.IStreamPipelineBehavior`2";
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                   SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                   SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor[] Rules =
    {
        Rule("ZEN0001", "Missing handler"), Rule("ZEN0002", "Duplicate handler"),
        Rule("ZEN0003", "Unsupported contract"), Rule("ZEN0004", "Invalid pipeline"),
        Rule("ZEN0005", "Invalid mediator declaration"),
        Rule("ZEN0006", "Conflicting generation modes"), Rule("ZEN0007", "Generated name collision"),
        Rule("ZEN0008", "Conflicting request kinds"), Rule("ZEN0009", "Unsupported generic binding"),
        Rule("ZEN0010", "Ambiguous generic binding"), Rule("ZEN0011", "Incompatible constraints"),
        Rule("ZEN0012", "Invalid ref route"), Rule("ZEN0013", "Invalid handler registration"),
        Rule("ZEN0014", "Unsupported notification erasure"), Rule("ZEN0015", "Conflicting configuration source"),
        Rule("ZEN0016", "Conflicting configuration structure"),         Rule("ZEN0017", "Unsupported configuration expression"),
        Rule("ZEN0018", "Invalid configuration value"), Rule("ZEN0019", "Ambiguous registration binding"),
        Rule("ZEN0020", "Interception connection failure")
    };

    private static DiagnosticDescriptor Rule(string id, string title) =>
        new(id, title, "{0}", "Zendiator", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Zendiator.GenerateZendiatorAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol).Collect();
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.Text: "AddZendiator" } }
            },
            static (ctx, _) => (InvocationExpressionSyntax)ctx.Node).Collect();
        var result = declarations.Combine(invocations).Combine(context.CompilationProvider)
            .Select(static (input, ct) => Generate(input.Left.Left, input.Left.Right, input.Right, ct));

        // Keep symbols in semantic analysis only; source output is a value-equatable string.
        var source = result.Select(static (r, _) => r.Source).WithTrackingName("SourceText");
        context.RegisterSourceOutput(source, static (ctx, text) =>
        {
            if (text.Length != 0)
                ctx.AddSource("Zendiator.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var interceptors = result.Select(static (r, _) => r.Interceptors).WithTrackingName("InterceptorsText");
        context.RegisterSourceOutput(interceptors, static (ctx, text) =>
        {
            if (text.Length != 0)
                ctx.AddSource("Zendiator.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        context.RegisterSourceOutput(result, static (ctx, r) =>
        {
            foreach (var diagnostic in r.Diagnostics)
                ctx.ReportDiagnostic(diagnostic);
        });
    }

    private sealed class Result(string source, string interceptors, List<Diagnostic> diagnostics)
    {
        public string Source { get; } = source;
        public string Interceptors { get; } = interceptors;
        public List<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    private sealed class Route(INamedTypeSymbol request, ITypeSymbol response, INamedTypeSymbol handler, bool isVoid = false, bool isSync = false)
    {
        public INamedTypeSymbol Request { get; } = request;
        public ITypeSymbol Response { get; } = response;
        public INamedTypeSymbol Handler { get; } = handler;
        public bool IsVoid { get; } = isVoid;
        public bool IsSync { get; } = isSync;
        public List<INamedTypeSymbol> Behaviors { get; } = new();
        public bool IsOpen { get; set; }
        public List<string> OpenTypeParams { get; } = new();
        public List<string> MethodConstraints { get; } = new();
        public string RequestDisplay { get; set; } = "";
        public string ResponseDisplay { get; set; } = "";
        public string HandlerDisplay { get; set; } = "";
        public string HandlerContractDisplay { get; set; } = "";
        public List<string> BehaviorDisplays { get; } = new();
        public List<string> BehaviorContractDisplays { get; } = new();
    }

    private sealed class OpenBinding(
        INamedTypeSymbol definition,
        INamedTypeSymbol requestDefinition,
        ITypeSymbol responsePattern,
        int[] patternMap,
        ITypeSymbol[] patternFixed,
        int[] handlerToDef,
        int[] handlerPosition,
        bool isVoid)
    {
        public INamedTypeSymbol Definition { get; } = definition;
        public INamedTypeSymbol RequestDefinition { get; } = requestDefinition;
        public ITypeSymbol ResponsePattern { get; } = responsePattern;
        public int[] PatternMap { get; } = patternMap;
        public ITypeSymbol[] PatternFixed { get; } = patternFixed;
        public int[] HandlerToDef { get; } = handlerToDef;
        public int[] HandlerPosition { get; } = handlerPosition;
        public bool IsVoid { get; } = isVoid;
        public MergedParam[] Merged { get; set; } = [];
    }

    private sealed class MultiBranch
    {
        public INamedTypeSymbol Handler { get; set; } = null!;
        public int Order;
        public string AssemblyId = "";
        public string FullName = "";
        public bool HandlerIsOpen;
        public string HandlerDisplay = "";
        public string HandlerContractDisplay = "";
        public List<INamedTypeSymbol> Behaviors { get; } = new();
        public List<string> BehaviorDisplays { get; } = new();
        public List<string> BehaviorContractDisplays { get; } = new();
    }

    private sealed class MultiRoute(INamedTypeSymbol request, ITypeSymbol response, bool isVoid, bool isOpen, bool isSync = false)
    {
        public INamedTypeSymbol Request { get; } = request;
        public ITypeSymbol Response { get; } = response;
        public bool IsVoid { get; } = isVoid;
        public bool IsOpen { get; } = isOpen;
        public bool IsSync { get; } = isSync;
        public List<MultiBranch> Branches { get; } = new();
        public List<string> OpenTypeParams { get; } = new();
        public List<string> MethodConstraints { get; } = new();
        public string RequestDisplay { get; set; } = "";
        public string ResponseDisplay { get; set; } = "";
    }

    private sealed class Subscriber(INamedTypeSymbol handler, int order, string assemblyId, string fullName)
    {
        public INamedTypeSymbol Handler { get; } = handler;
        public int Order { get; } = order;
        public string AssemblyId { get; } = assemblyId;
        public string FullName { get; } = fullName;
    }

    private sealed class NotificationRoute(INamedTypeSymbol notification, bool isOpen)
    {
        public INamedTypeSymbol Notification { get; } = notification;
        public bool IsOpen { get; } = isOpen;
        public List<Subscriber> Subscribers { get; } = new();
        public List<string> OpenTypeParams { get; } = new();
        public List<string> MethodConstraints { get; } = new();
        public string NotificationDisplay { get; set; } = "";
        public List<string> HandlerDisplays { get; } = new();
        public List<string> HandlerContractDisplays { get; } = new();
    }

    private sealed class DiSetting
    {
        public string? Namespace;
        public Location? NamespaceLocation;
        public List<(INamedTypeSymbol Type, string Key)> Markers { get; } = new();
        public List<string> Assemblies { get; } = new();
        public List<(INamedTypeSymbol Type, bool IsOpen, int Order, string Key)> Behaviors { get; } = new();
        public List<(INamedTypeSymbol Type, string Key)> Notifications { get; } = new();
        public List<(INamedTypeSymbol Type, string Key, int Order)> HandlerOrders { get; } = new();
        public Location CallLocation = Location.None;

        public string StructureKey()
        {
            // Assemblies normalize to one identity set regardless of the spelling
            // (marker type vs typeof(X).Assembly), matching discovery semantics.
            var assemblies = Markers.Select(static entry => entry.Type.ContainingAssembly.Identity.ToString()).Concat(Assemblies)
                .Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToList();
            var parts = new List<string>
            {
                "ns=" + (Namespace ?? ""),
                "assemblies=" + string.Join(",", assemblies),
                "behaviors=" + string.Join(",", Behaviors.OrderBy(static e => e.Order).ThenBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Order + ":" + e.Key)),
                "notifications=" + string.Join(",", Notifications.OrderBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Key)),
                "orders=" + string.Join(",", HandlerOrders.OrderBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Key + ":" + e.Order)),            };
            return string.Join(";", parts);
        }
    }

    private static string? FullNameOf(ITypeSymbol type)
    {
        // Match Type.ToString(): reference and implementation assemblies can
        // differ for forwarded generic arguments (for example System.Int32).
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FullNameOf(array.ElementType) is string element
                    ? element + "[" + new string(',', array.Rank - 1) + "]"
                    : null;
            case INamedTypeSymbol named when named.IsUnboundGenericType:
                return QualifiedMetadataName(named.OriginalDefinition);
            case INamedTypeSymbol named when named.IsGenericType && !IsOpenDefinition(named):
                {
                    var definition = QualifiedMetadataName(named.OriginalDefinition);
                    if (definition == null) return null;
                    var args = new List<string>();
                    foreach (var argument in named.TypeArguments)
                    {
                        var name = FullNameOf(argument);
                        if (name == null) return null;
                        args.Add(name);
                    }
                    return definition + "[" + string.Join(",", args) + "]";
                }
            case INamedTypeSymbol named:
                return QualifiedMetadataName(named.OriginalDefinition);
            default:
                return null;
        }
    }

    private static string? QualifiedMetadataName(INamedTypeSymbol definition)
    {
        if (definition.ContainingNamespace == null) return null;
        var chain = new Stack<string>();
        for (var t = definition; t != null; t = t.ContainingType) chain.Push(t.MetadataName);
        var ns = definition.ContainingNamespace.IsGlobalNamespace ? "" : definition.ContainingNamespace.ToDisplayString() + ".";
        return ns + string.Join("+", chain);
    }

    private static Result Generate(ImmutableArray<INamedTypeSymbol> declarations, ImmutableArray<InvocationExpressionSyntax> diInvocations, Compilation compilation, CancellationToken ct)
    {
        var errors = new List<Diagnostic>();
        void Error(int rule, string message, ISymbol? symbol = null) => errors.Add(Diagnostic.Create(
            Rules[rule - 1], symbol?.Locations.FirstOrDefault(static l => l.IsInSource) ??
            declarations.FirstOrDefault()?.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None, message));

        var assemblyGenAttributes = compilation.Assembly.GetAttributes()
            .Where(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.GenerateZendiatorAttribute").ToArray();
        if (assemblyGenAttributes.Length > 1)
        {
            Error(6, "Only one assembly-level [GenerateZendiator] attribute is allowed per compilation.");
            return new Result("", "", errors);
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
        if (declarations.Length == 0 && assemblyGen == null && !hasBareDiCall && !hasLambdaDiCall) return new Result("", "", errors);
        if (declarations.Length != 0 && assemblyGen != null)
        {
            Error(6, "Cannot combine class-level [GenerateZendiator] with assembly-level [GenerateZendiator]. Keep one configuration mode.", assemblyGen.AttributeClass);
            return new Result("", "", errors);
        }
        if (declarations.Length > 1)
        {
            Error(5, "Exactly one [GenerateZendiator] declaration is allowed per compilation.");
            return new Result("", "", errors);
        }
        var assemblyMode = assemblyGen != null;

        string? assemblyNamespace = null;
        INamedTypeSymbol? mediator = null;
        if (assemblyMode)
        {
            assemblyNamespace = ResolveAssemblyNamespace(assemblyGen!, compilation, errors);
            if (assemblyNamespace == null) return new Result("", "", errors);
        }
        else if (declarations.Length != 0)
        {
            mediator = declarations[0];
            var classGenAttr = mediator.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.GenerateZendiatorAttribute");
            if (classGenAttr != null && classGenAttr.NamedArguments.Any(static p => p.Key == "Namespace" && p.Value.Value is string s && s.Length != 0))
            {
                Error(5, "Namespace is only valid on assembly-level [GenerateZendiator].", mediator);
                return new Result("", "", errors);
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
                return new Result("", "", errors);
            }
        }

        void ErrorAt(int rule, string message, Location location) =>
            errors.Add(Diagnostic.Create(Rules[rule - 1], location, message));

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
        var diMode = !hasAttributeConfig && (hasBareDiCall || hasLambdaDiCall);
        string? diNamespace = null;
        var diAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var diPipelines = new List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)>();
        var diNotifications = new List<INamedTypeSymbol>();
        var diOpenNotifications = new List<INamedTypeSymbol>();
        var diHandlerOrders = new Dictionary<string, (int Order, Location Location)>(StringComparer.Ordinal);
        var diCallSites = new List<(int Version, string Data, bool IsLambda, bool IsExtensionForm)>();
        string diFingerprint = "";
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

        DiSetting? AnalyzeDiLambda(InvocationExpressionSyntax invocation, LambdaExpressionSyntax lambda, SemanticModel model)
        {
            if (lambda.AsyncKeyword != default)
            {
                ErrorAt(17, "AddZendiator configuration must be a synchronous lambda. Remove async.", lambda.GetLocation());
                return null;
            }
            string? parameterName = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
                ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList?.Parameters.Count == 1 => parenthesized.ParameterList.Parameters[0].Identifier.Text,
                _ => null,
            };
            if (parameterName == null)
            {
                ErrorAt(17, "AddZendiator configuration lambda must take exactly one parameter.", lambda.GetLocation());
                return null;
            }
            var setting = new DiSetting { CallLocation = invocation.GetLocation() };
            var valid = true;
            if (lambda.Body is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!AnalyzeDiStatement(statement, parameterName, model, setting)) valid = false;
                }
            }
            else if (lambda.Body is ExpressionSyntax expression)
            {
                if (expression is InvocationExpressionSyntax single &&
                    single.Expression is MemberAccessExpressionSyntax singleAccess &&
                    singleAccess.Expression is IdentifierNameSyntax singleTarget &&
                    singleTarget.Identifier.Text == parameterName)
                {
                    if (!AnalyzeDiCall(single, singleAccess.Name.Identifier.Text, model, setting)) valid = false;
                }
                else if (expression is AssignmentExpressionSyntax assignment &&
                    assignment.Left is MemberAccessExpressionSyntax assignAccess &&
                    assignAccess.Expression is IdentifierNameSyntax assignTarget &&
                    assignTarget.Identifier.Text == parameterName)
                {
                    if (!AnalyzeDiAssignment(assignAccess.Name.Identifier.Text, assignment.Right, model, setting)) valid = false;
                }
                else
                {
                    ErrorAt(17, "AddZendiator configuration lambda must be a block of recorder calls or a single recorder call or assignment.", lambda.Body.GetLocation());
                    return null;
                }
            }
            else
            {
                ErrorAt(17, "AddZendiator configuration lambda must be a block of recorder calls or a single recorder call.", lambda.Body.GetLocation());
                return null;
            }
            return valid ? setting : null;
        }

        bool AnalyzeDiStatement(StatementSyntax statement, string parameterName, SemanticModel model, DiSetting setting)
        {
            if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
                invocation.Expression is MemberAccessExpressionSyntax access &&
                access.Expression is IdentifierNameSyntax target &&
                target.Identifier.Text == parameterName)
            {
                return AnalyzeDiCall(invocation, access.Name.Identifier.Text, model, setting);
            }
            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                assignment.Left is MemberAccessExpressionSyntax left &&
                left.Expression is IdentifierNameSyntax leftTarget &&
                leftTarget.Identifier.Text == parameterName)
            {
                return AnalyzeDiAssignment(left.Name.Identifier.Text, assignment.Right, model, setting);
            }
            ErrorAt(17, "Unsupported AddZendiator configuration statement. Use direct recorder calls (RegisterServicesFromAssemblyContaining, AddOpenBehavior, AddOpenStreamBehavior, AddNotification, ConfigureHandlerOrder) and property assignments (Namespace, ServiceLifetime).", statement.GetLocation());
            return false;
        }

        bool AnalyzeDiCall(InvocationExpressionSyntax invocation, string methodName, SemanticModel model, DiSetting setting)
        {
            var location = invocation.GetLocation();
            INamedTypeSymbol? TypeArgument(int index)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax access) return null;
                if (access.Name is not GenericNameSyntax generic) return null;
                if (generic.TypeArgumentList.Arguments.Count <= index) return null;
                return model.GetSymbolInfo(generic.TypeArgumentList.Arguments[index], ct).Symbol as INamedTypeSymbol;
            }
            int? IntArgument(int index)
            {
                if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count <= index) return null;
                var constant = model.GetConstantValue(invocation.ArgumentList.Arguments[index].Expression, ct);
                return constant.HasValue && constant.Value is int value ? value : (int?)null;
            }
            INamedTypeSymbol? TypeofArgument(int index, out bool supported)
            {
                supported = true;
                if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count <= index)
                {
                    supported = false;
                    return null;
                }
                var expression = invocation.ArgumentList.Arguments[index].Expression;
                if (expression is TypeOfExpressionSyntax { Type: { } type })
                    return model.GetSymbolInfo(type, ct).Symbol as INamedTypeSymbol;
                supported = false;
                return null;
            }
            switch (methodName)
            {
                case "RegisterServicesFromAssemblyContaining":
                    {
                        if (invocation.ArgumentList?.Arguments.Count != 0) goto Unsupported;
                        var marker = TypeArgument(0);
                        if (marker != null && marker.IsUnboundGenericType) marker = marker.OriginalDefinition;
                        var key = marker == null ? null : FullNameOf(marker);
                        if (key == null) goto Unsupported;
                        if (!setting.Markers.Any(entry => entry.Key == key))
                            setting.Markers.Add((marker!, key));
                        return true;
                    }
                case "RegisterServicesFromAssembly":
                    {
                        if (invocation.ArgumentList?.Arguments.Count != 1) goto Unsupported;
                        var expression = invocation.ArgumentList.Arguments[0].Expression;
                        if (expression is MemberAccessExpressionSyntax
                            {
                                Name.Identifier.Text: "Assembly",
                                Expression: TypeOfExpressionSyntax { Type: { } type }
                            } &&
                            model.GetSymbolInfo(type, ct).Symbol is INamedTypeSymbol target &&
                            target.ContainingAssembly != null)
                        {
                            var identity = target.ContainingAssembly.Identity.ToString();
                            if (!setting.Assemblies.Contains(identity)) setting.Assemblies.Add(identity);
                            return true;
                        }
                        goto Unsupported;
                    }
                case "AddOpenBehavior":
                case "AddOpenStreamBehavior":
                    {
                        if (invocation.ArgumentList?.Arguments.Count != 2) goto Unsupported;
                        var behavior = TypeofArgument(0, out var supported);
                        var order = IntArgument(1);
                        if (!supported || behavior == null || order == null) goto Unsupported;
                        var key = FullNameOf(behavior);
                        if (key == null) goto Unsupported;
                        var isOpen = behavior.IsUnboundGenericType;
                        if (setting.Behaviors.Any(entry => entry.Key == key))
                        {
                            ErrorAt(18, $"Behavior {key} is configured more than once. Register each behavior type once.", location);
                            return false;
                        }
                        if (setting.Behaviors.Any(entry => entry.Order == order))
                        {
                            ErrorAt(18, $"Behavior order {order} is used more than once. Order values must be unique.", location);
                            return false;
                        }
                        setting.Behaviors.Add((isOpen ? behavior.OriginalDefinition : behavior, isOpen, order.Value, key));
                        return true;
                    }
                case "AddNotification":
                    {
                        if (invocation.ArgumentList?.Arguments.Count != 0) goto Unsupported;
                        var notification = TypeArgument(0);
                        if (notification != null && notification.IsUnboundGenericType) notification = notification.OriginalDefinition;
                        var key = notification == null ? null : FullNameOf(notification);
                        if (key == null) goto Unsupported;
                        if (setting.Notifications.Any(entry => entry.Key == key))
                        {
                            ErrorAt(18, $"Notification {key} is declared more than once.", location);
                            return false;
                        }
                        setting.Notifications.Add((notification!, key));
                        return true;
                    }
                case "ConfigureHandlerOrder":
                    {
                        if (invocation.ArgumentList?.Arguments.Count != 2) goto Unsupported;
                        var handler = TypeofArgument(0, out var supported);
                        var order = IntArgument(1);
                        if (!supported || handler == null || order == null) goto Unsupported;
                        if (handler.IsUnboundGenericType) handler = handler.OriginalDefinition;
                        var key = FullNameOf(handler);
                        if (key == null) goto Unsupported;
                        if (setting.HandlerOrders.Any(entry => entry.Key == key))
                        {
                            ErrorAt(18, $"Handler {key} has more than one configured order.", location);
                            return false;
                        }
                        setting.HandlerOrders.Add((handler, key, order.Value));
                        return true;
                    }
                default:
                    ErrorAt(17, $"Unsupported AddZendiator configuration call '{methodName}'. Supported calls: RegisterServicesFromAssemblyContaining, RegisterServicesFromAssembly, AddOpenBehavior, AddOpenStreamBehavior, AddNotification, ConfigureHandlerOrder.", location);
                    return false;
            }
        Unsupported:
            ErrorAt(17, $"Unsupported AddZendiator configuration shape for '{methodName}'. Use typeof arguments, generic type arguments, and constant values as documented.", location);
            return false;
        }

        bool AnalyzeDiAssignment(string propertyName, ExpressionSyntax value, SemanticModel model, DiSetting setting)
        {
            var location = value.GetLocation();
            switch (propertyName)
            {
                case "Namespace":
                    {
                        var constant = model.GetConstantValue(value, ct);
                        if (!constant.HasValue || constant.Value == null)
                        {
                            if (constant.HasValue)
                            {
                                setting.Namespace = null;
                                setting.NamespaceLocation = location;
                                return true;
                            }
                            ErrorAt(17, "Namespace must be a string constant. Assign a literal, const, or null.", location);
                            return false;
                        }
                        if (constant.Value is not string text)
                        {
                            ErrorAt(18, "Namespace must be a string.", location);
                            return false;
                        }
                        if (setting.NamespaceLocation != null)
                        {
                            ErrorAt(18, "Namespace is assigned more than once. Assign it once.", location);
                            return false;
                        }
                        if (!IsValidNamespace(text))
                        {
                            ErrorAt(18, $"Invalid generation namespace '{text}'. Use dot-separated identifiers.", location);
                            return false;
                        }
                        setting.Namespace = text;
                        setting.NamespaceLocation = location;
                        return true;
                    }
                case "ServiceLifetime":
                    {
                        // Lifetime flows at runtime and never shapes generation; only the
                        // value domain is checked here.
                        var constant = model.GetConstantValue(value, ct);
                        if (constant.HasValue && constant.Value is int lifetime &&
                            lifetime is not (0 or 1 or 2))
                        {
                            ErrorAt(18, "ServiceLifetime must be Transient, Scoped, or Singleton.", location);
                            return false;
                        }
                        return true;
                    }
                default:
                    ErrorAt(17, $"Unsupported AddZendiator configuration property '{propertyName}'. Supported properties: Namespace, ServiceLifetime.", location);
                    return false;
            }
        }

        string Position(Location location)
        {
            var span = location.GetLineSpan();
            return span.Path + "(" + (span.StartLinePosition.Line + 1) + "," + (span.StartLinePosition.Character + 1) + ")";
        }

        var requestDefinition = compilation.GetTypeByMetadataName(Request);
        var handlerDefinition = compilation.GetTypeByMetadataName(Handler);
        var behaviorDefinition = compilation.GetTypeByMetadataName(Behavior);
        var voidRequestDefinition = compilation.GetTypeByMetadataName(VoidRequest);
        var voidHandlerDefinition = compilation.GetTypeByMetadataName(VoidHandler);
        var voidBehaviorDefinition = compilation.GetTypeByMetadataName(VoidBehavior);
        var notificationDefinition = compilation.GetTypeByMetadataName(Notification);
        var notificationHandlerDefinition = compilation.GetTypeByMetadataName(NotificationHandler);
        var multiResponseDefinition = compilation.GetTypeByMetadataName(MultiResponse);
        var multiVoidDefinition = compilation.GetTypeByMetadataName(MultiVoid);
        var syncRequestDefinition = compilation.GetTypeByMetadataName(SyncRequest);
        var syncVoidRequestDefinition = compilation.GetTypeByMetadataName(SyncVoidRequest);
        var syncHandlerDefinition = compilation.GetTypeByMetadataName(SyncHandler);
        var syncVoidHandlerDefinition = compilation.GetTypeByMetadataName(SyncVoidHandler);
        var syncBehaviorDefinition = compilation.GetTypeByMetadataName(SyncBehavior);
        var syncVoidBehaviorDefinition = compilation.GetTypeByMetadataName(SyncVoidBehavior);
        var syncMultiResponseDefinition = compilation.GetTypeByMetadataName(SyncMultiResponse);
        var syncMultiVoidDefinition = compilation.GetTypeByMetadataName(SyncMultiVoid);
        var streamRequestDefinition = compilation.GetTypeByMetadataName(StreamRequest);
        var streamHandlerDefinition = compilation.GetTypeByMetadataName(StreamHandler);
        var streamBehaviorDefinition = compilation.GetTypeByMetadataName(StreamBehavior);
        if (requestDefinition == null || handlerDefinition == null || behaviorDefinition == null ||
            voidRequestDefinition == null || voidHandlerDefinition == null || voidBehaviorDefinition == null ||
            notificationDefinition == null || notificationHandlerDefinition == null ||
            multiResponseDefinition == null || multiVoidDefinition == null ||
            syncRequestDefinition == null || syncVoidRequestDefinition == null ||
            syncHandlerDefinition == null || syncVoidHandlerDefinition == null ||
            syncBehaviorDefinition == null || syncVoidBehaviorDefinition == null ||
            syncMultiResponseDefinition == null || syncMultiVoidDefinition == null ||
            streamRequestDefinition == null || streamHandlerDefinition == null || streamBehaviorDefinition == null)
        {
            Error(3, "Reference Zendiator.Abstractions.");
            return new Result("", "", errors);
        }

        var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { compilation.Assembly };
        var pipelines = new List<(INamedTypeSymbol Type, int Order, bool IsOpenBehavior)>();
        if (diMode)
        {
            foreach (var assembly in diAssemblies) assemblies.Add(assembly);
            foreach (var pipeline in diPipelines) pipelines.Add(pipeline);
        }
        ISymbol accessContext = assemblyMode || diMode ? compilation.Assembly : mediator!;
        var configAttributes = assemblyMode
            ? compilation.Assembly.GetAttributes().AsEnumerable()
            : mediator != null ? mediator.GetAttributes().AsEnumerable() : Enumerable.Empty<AttributeData>();
        foreach (var attribute in configAttributes)
        {
            ct.ThrowIfCancellationRequested();
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name == "Zendiator.IncludeAssemblyAttribute")
            {
                if (attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol marker)
                    assemblies.Add(marker.ContainingAssembly);
                else Error(5, "IncludeAssembly requires a marker type.", mediator);
            }
            if (name != "Zendiator.PipelineBehaviorAttribute") continue;
            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol type)
            {
                Error(4, "PipelineBehavior requires an implementation type.", mediator);
                continue;
            }
            var order = attribute.NamedArguments.FirstOrDefault(p => p.Key == "Order").Value.Value as int? ?? 0;
            if (pipelines.Any(p => p.Order == order || Same(p.Type, type)))
            {
                Error(4, "Pipeline types and Order values must be unique.", mediator);
                continue;
            }
            pipelines.Add((type, order, type.IsUnboundGenericType));
        }

        var allTypes = assemblies.SelectMany(a => Types(a.GlobalNamespace, ct)).OrderBy(Name, StringComparer.Ordinal).ToArray();
        var requests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var handlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var voidHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var openRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var openHandlers = new List<OpenBinding>();
        var openVoidHandlers = new List<OpenBinding>();
        var openSyncRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var openSyncHandlers = new List<OpenBinding>();
        var openSyncVoidHandlers = new List<OpenBinding>();
        var syncRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var syncHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var syncVoidHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var syncVoidRequests = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var streamRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var streamHandlers = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var openStreamRequests = new Dictionary<INamedTypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var openStreamHandlers = new List<OpenBinding>();
        var knownNotifications = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var openNotifications = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var subscribers = new Dictionary<INamedTypeSymbol, List<Subscriber>>(SymbolEqualityComparer.Default);
        var openSubscribers = new List<OpenBinding>();

        int AttributeOrder(AttributeData attribute)
        {
            foreach (var pair in attribute.NamedArguments)
            {
                if (pair.Key == "Order" && pair.Value.Value is int named) return named;
            }
            if (attribute.ConstructorArguments.FirstOrDefault().Value is int constructed) return constructed;
            return 0;
        }

        int SubscriberOrder(INamedTypeSymbol handler)
        {
            if (FullNameOf(handler) is string key && diHandlerOrders.TryGetValue(key, out var configured)) return configured.Order;
            if (handler.IsGenericType && FullNameOf(handler.OriginalDefinition) is string definition &&
                diHandlerOrders.TryGetValue(definition, out var configuredDefinition)) return configuredDefinition.Order;
            var attr = handler.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == "Zendiator.HandlerOrderAttribute");
            if (attr == null) return 0;
            foreach (var pair in attr.NamedArguments)
            {
                if (pair.Key == "Order" && pair.Value.Value is int named) return named;
            }
            if (attr.ConstructorArguments.FirstOrDefault().Value is int constructed) return constructed;
            return 0;
        }

        void AddSubscriber(INamedTypeSymbol notification, INamedTypeSymbol handler)
        {
            if (!subscribers.TryGetValue(notification, out var list)) subscribers[notification] = list = new();
            if (list.Any(s => Same(s.Handler, handler))) return;
            list.Add(new Subscriber(handler, SubscriberOrder(handler), handler.ContainingAssembly.Identity.ToString(), Name(handler)));
        }

        bool EnsureOpenNotification(INamedTypeSymbol reqDef)
        {
            if (openNotifications.Contains(reqDef)) return true;
            if (!IsOpenDefinition(reqDef) || !IsPublicDefinition(reqDef) ||
                !reqDef.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
            {
                Error(3, $"Notification {Name(reqDef)} must be a public generic definition implementing Zendiator.INotification.", reqDef);
                return false;
            }
            openNotifications.Add(reqDef);
            return true;
        }

        void CollectOpenSubscriber(INamedTypeSymbol type, INamedTypeSymbol contract)
        {
            if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
                !compilation.IsSymbolAccessibleWithin(type, accessContext))
            {
                Error(3, $"Subscriber {Name(type)} must be an accessible class without a generic container.", type);
                return;
            }
            if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
            {
                Error(3, $"Subscriber {Name(type)} must target a concrete notification pattern.", type);
                return;
            }
            if (!pattern.IsGenericType)
            {
                Error(9, $"Subscriber {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every subscriber type parameter to a notification type argument.", type);
                return;
            }
            var reqDef = pattern.OriginalDefinition;
            var pargs = pattern.TypeArguments;
            var pmap = new int[pargs.Length];
            var pfixed = new ITypeSymbol[pargs.Length];
            var h2d = new int[type.TypeParameters.Length];
            var hpos = new int[type.TypeParameters.Length];
            for (var j = 0; j < h2d.Length; j++) h2d[j] = -1;
            for (var j = 0; j < hpos.Length; j++) hpos[j] = -1;
            for (var i = 0; i < pargs.Length; i++)
            {
                if (pargs[i] is ITypeParameterSymbol hp && Same(hp.ContainingSymbol, type))
                {
                    if (hpos[hp.Ordinal] != -1)
                    {
                        Error(9, $"Subscriber {Name(type)} maps {hp.Name} to multiple notification type arguments. Give each subscriber type parameter one position.", type);
                        return;
                    }
                    pmap[i] = i;
                    h2d[hp.Ordinal] = i;
                    hpos[hp.Ordinal] = i;
                }
                else
                {
                    Error(9, $"Subscriber {Name(type)} must use a type parameter for every pattern argument of {Name(pattern)}. Fixed open notification patterns are not supported; add a closed subscriber instead.", type);
                    return;
                }
            }
            for (var j = 0; j < h2d.Length; j++)
            {
                if (h2d[j] < 0)
                {
                    Error(9, $"Subscriber {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}.", type);
                    return;
                }
            }
            if (!EnsureOpenNotification(reqDef)) return;
            if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
            {
                Error(12, $"Subscriber {Name(type)} has ref-like type parameters. Ref struct notifications are not supported.", type);
                return;
            }
            var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
            var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
            for (var j = 0; j < type.TypeParameters.Length; j++)
            {
                if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
                {
                    Error(11, $"Subscriber {Name(type)} declares constraints that notification {Name(reqDef)} does not satisfy.", type);
                    return;
                }
            }
            openSubscribers.Add(new OpenBinding(type, reqDef, compilation.GetSpecialType(SpecialType.System_Void), pmap, pfixed, h2d, hpos, isVoid: true) { Merged = merged });
        }

        bool EnsureOpenRequest(INamedTypeSymbol reqDef)
        {
            if (openRequests.ContainsKey(reqDef)) return true;
            if (!IsOpenDefinition(reqDef) || reqDef.IsRefLikeType)
            {
                Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract.", reqDef);
                return false;
            }
            var rc = reqDef.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
            if (rc.Length != 1 || !IsPublicDefinition(reqDef) || !LeavesPublicOrParam(rc[0].TypeArguments[0], reqDef))
            {
                Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract over public or type-parameter types.", reqDef);
                return false;
            }
            if (reqDef.TypeParameters.Any(static p => p.AllowsRefLikeType))
            {
                Error(12, $"Request {Name(reqDef)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", reqDef);
                return false;
            }
            openRequests[reqDef] = rc[0].TypeArguments[0];
            return true;
        }

        void CollectOpenHandler(INamedTypeSymbol type, INamedTypeSymbol contract, List<OpenBinding> bucket, bool isVoid)
        {
            if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
                !compilation.IsSymbolAccessibleWithin(type, accessContext))
            {
                Error(3, $"Handler {Name(type)} must be an accessible class without a generic container.", type);
                return;
            }
            if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
            {
                Error(3, $"Handler {Name(type)} must target a concrete request pattern.", type);
                return;
            }
            if (!pattern.IsGenericType)
            {
                Error(9, $"Handler {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                return;
            }
            var reqDef = pattern.OriginalDefinition;
            var pargs = pattern.TypeArguments;
            var pmap = new int[pargs.Length];
            var pfixed = new ITypeSymbol[pargs.Length];
            var h2d = new int[type.TypeParameters.Length];
            var hpos = new int[type.TypeParameters.Length];
            for (var j = 0; j < h2d.Length; j++) h2d[j] = -1;
            for (var j = 0; j < hpos.Length; j++) hpos[j] = -1;
            for (var i = 0; i < pargs.Length; i++)
            {
                if (pargs[i] is ITypeParameterSymbol hp && Same(hp.ContainingSymbol, type))
                {
                    if (hpos[hp.Ordinal] != -1)
                    {
                        Error(9, $"Handler {Name(type)} maps {hp.Name} to multiple request type arguments. Give each handler type parameter one position.", type);
                        return;
                    }
                    pmap[i] = i;
                    h2d[hp.Ordinal] = i;
                    hpos[hp.Ordinal] = i;
                }
                else if (!HasOpenArguments(pargs[i]) && Public(pargs[i]))
                {
                    pmap[i] = -1;
                    pfixed[i] = pargs[i];
                }
                else
                {
                    Error(9, $"Handler {Name(type)} has an unsupported pattern argument {Name(pargs[i])}. Use a handler type parameter or a concrete type.", type);
                    return;
                }
            }
            for (var j = 0; j < h2d.Length; j++)
            {
                if (h2d[j] < 0)
                {
                    Error(9, $"Handler {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                    return;
                }
            }
            if (!EnsureOpenRequest(reqDef)) return;
            ITypeSymbol responsePattern = compilation.GetSpecialType(SpecialType.System_Void);
            if (!isVoid)
            {
                responsePattern = contract.TypeArguments[1];
                if (!LeavesPublicOrParam(responsePattern, type))
                {
                    Error(3, $"Handler {Name(type)} response {Name(responsePattern)} must be public or built from handler type parameters.", type);
                    return;
                }
                var mapped = Substitute(openRequests[reqDef], reqDef, pargs.ToArray(), compilation);
                if (!Same(mapped, responsePattern))
                {
                    Error(3, $"Handler {Name(type)} response {Name(responsePattern)} does not match request {Name(pattern)} response {Name(mapped)}.", type);
                    return;
                }
            }
            else if (!pattern.AllInterfaces.Any(i => Same(i.OriginalDefinition, voidRequestDefinition)))
            {
                Error(3, $"Handler {Name(type)} request {Name(pattern)} must implement Zendiator.IRequest.", type);
                return;
            }
            if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
            {
                Error(12, $"Handler {Name(type)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", type);
                return;
            }
            var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
            var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
            for (var j = 0; j < type.TypeParameters.Length; j++)
            {
                if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
                {
                    Error(11, $"Handler {Name(type)} declares constraints that request {Name(reqDef)} does not satisfy. Loosen the handler or tighten the request.", type);
                    return;
                }
            }
            bucket.Add(new OpenBinding(type, reqDef, responsePattern, pmap, pfixed, h2d, hpos, isVoid) { Merged = merged });
        }

        bool EnsureOpenSyncRequest(INamedTypeSymbol reqDef)
        {
            if (openSyncRequests.ContainsKey(reqDef)) return true;
            if (!IsOpenDefinition(reqDef))
            {
                Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract.", reqDef);
                return false;
            }
            var rc = reqDef.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncRequestDefinition)).ToArray();
            if (rc.Length != 1 || !IsPublicDefinition(reqDef) || !LeavesPublicOrParam(rc[0].TypeArguments[0], reqDef))
            {
                Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract over public or type-parameter types.", reqDef);
                return false;
            }
            if (MayBeRefLike(rc[0].TypeArguments[0], reqDef))
            {
                Error(12, $"Request {Name(reqDef)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", reqDef);
                return false;
            }
            openSyncRequests[reqDef] = rc[0].TypeArguments[0];
            return true;
        }

        void CollectOpenSyncHandler(INamedTypeSymbol type, INamedTypeSymbol contract, List<OpenBinding> bucket, bool isVoid)
        {
            if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
                !compilation.IsSymbolAccessibleWithin(type, accessContext))
            {
                Error(3, $"Handler {Name(type)} must be an accessible class without a generic container.", type);
                return;
            }
            if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
            {
                Error(3, $"Handler {Name(type)} must target a concrete request pattern.", type);
                return;
            }
            if (!pattern.IsGenericType)
            {
                Error(9, $"Handler {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                return;
            }
            var reqDef = pattern.OriginalDefinition;
            var pargs = pattern.TypeArguments;
            var pmap = new int[pargs.Length];
            var pfixed = new ITypeSymbol[pargs.Length];
            var h2d = new int[type.TypeParameters.Length];
            var hpos = new int[type.TypeParameters.Length];
            for (var j = 0; j < h2d.Length; j++) h2d[j] = -1;
            for (var j = 0; j < hpos.Length; j++) hpos[j] = -1;
            for (var i = 0; i < pargs.Length; i++)
            {
                if (pargs[i] is ITypeParameterSymbol hp && Same(hp.ContainingSymbol, type))
                {
                    if (hpos[hp.Ordinal] != -1)
                    {
                        Error(9, $"Handler {Name(type)} maps {hp.Name} to multiple request type arguments. Give each handler type parameter one position.", type);
                        return;
                    }
                    pmap[i] = i;
                    h2d[hp.Ordinal] = i;
                    hpos[hp.Ordinal] = i;
                }
                else if (!HasOpenArguments(pargs[i]) && Public(pargs[i]))
                {
                    pmap[i] = -1;
                    pfixed[i] = pargs[i];
                }
                else
                {
                    Error(9, $"Handler {Name(type)} has an unsupported pattern argument {Name(pargs[i])}. Use a handler type parameter or a concrete type.", type);
                    return;
                }
            }
            for (var j = 0; j < h2d.Length; j++)
            {
                if (h2d[j] < 0)
                {
                    Error(9, $"Handler {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                    return;
                }
            }
            if (isVoid)
            {
                if (!IsOpenDefinition(reqDef) || !IsPublicDefinition(reqDef) ||
                    !reqDef.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
                {
                    Error(3, $"Request {Name(reqDef)} must be a public generic definition implementing ISyncRequest.", reqDef);
                    return;
                }
                openSyncRequests[reqDef] = compilation.GetSpecialType(SpecialType.System_Void);
            }
            else if (!EnsureOpenSyncRequest(reqDef)) return;
            ITypeSymbol responsePattern = openSyncRequests[reqDef];
            if (!isVoid)
            {
                responsePattern = contract.TypeArguments[1];
                if (!LeavesPublicOrParam(responsePattern, type))
                {
                    Error(3, $"Handler {Name(type)} response {Name(responsePattern)} must be public or built from handler type parameters.", type);
                    return;
                }
                if (MayBeRefLike(responsePattern, type))
                {
                    Error(12, $"Handler {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                    return;
                }
                var mapped = Substitute(openSyncRequests[reqDef], reqDef, pargs.ToArray(), compilation);
                if (!Same(mapped, responsePattern))
                {
                    Error(3, $"Handler {Name(type)} response {Name(responsePattern)} does not match request {Name(pattern)} response {Name(mapped)}.", type);
                    return;
                }
            }
            else if (!pattern.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
            {
                Error(8, $"Handler {Name(type)} targets response request {Name(pattern)}. Use two-argument ISyncRequestHandler<{Name(pattern)}, TResponse>.", type);
                return;
            }
            var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
            var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
            for (var j = 0; j < type.TypeParameters.Length; j++)
            {
                var reqAllows = reqDef.TypeParameters[h2d[j]].AllowsRefLikeType;
                var handlerAllows = type.TypeParameters[j].AllowsRefLikeType;
                if (reqAllows && !handlerAllows)
                {
                    Error(12, $"Handler {Name(type)} must allow ref struct type arguments where request {Name(reqDef)} allows them.", type);
                    return;
                }
                if (reqAllows) merged[h2d[j]].AllowsRefLike = true;
                if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
                {
                    Error(11, $"Handler {Name(type)} declares constraints that request {Name(reqDef)} does not satisfy. Loosen the handler or tighten the request.", type);
                    return;
                }
            }
            bucket.Add(new OpenBinding(type, reqDef, responsePattern, pmap, pfixed, h2d, hpos, isVoid) { Merged = merged });
        }

        bool EnsureOpenStreamRequest(INamedTypeSymbol reqDef)
        {
            if (openStreamRequests.ContainsKey(reqDef)) return true;
            if (!IsOpenDefinition(reqDef) || reqDef.IsRefLikeType)
            {
                Error(3, $"Stream request {Name(reqDef)} must be a public generic definition with exactly one item contract.", reqDef);
                return false;
            }
            var rc = reqDef.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
            if (rc.Length != 1 || !IsPublicDefinition(reqDef) || !LeavesPublicOrParam(rc[0].TypeArguments[0], reqDef))
            {
                Error(3, $"Stream request {Name(reqDef)} must be a public generic definition with exactly one item contract over public or type-parameter types.", reqDef);
                return false;
            }
            if (reqDef.TypeParameters.Any(static p => p.AllowsRefLikeType))
            {
                Error(12, $"Stream request {Name(reqDef)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", reqDef);
                return false;
            }
            if (MayBeRefLike(rc[0].TypeArguments[0], reqDef))
            {
                Error(12, $"Stream request {Name(reqDef)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", reqDef);
                return false;
            }
            openStreamRequests[reqDef] = rc[0].TypeArguments[0];
            return true;
        }

        void CollectOpenStreamHandler(INamedTypeSymbol type, INamedTypeSymbol contract)
        {
            if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
                !compilation.IsSymbolAccessibleWithin(type, accessContext))
            {
                Error(3, $"Stream handler {Name(type)} must be an accessible class without a generic container.", type);
                return;
            }
            if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
            {
                Error(3, $"Stream handler {Name(type)} must target a concrete request pattern.", type);
                return;
            }
            if (!pattern.IsGenericType)
            {
                Error(9, $"Stream handler {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                return;
            }
            var reqDef = pattern.OriginalDefinition;
            var pargs = pattern.TypeArguments;
            var pmap = new int[pargs.Length];
            var pfixed = new ITypeSymbol[pargs.Length];
            var h2d = new int[type.TypeParameters.Length];
            var hpos = new int[type.TypeParameters.Length];
            for (var j = 0; j < h2d.Length; j++) h2d[j] = -1;
            for (var j = 0; j < hpos.Length; j++) hpos[j] = -1;
            for (var i = 0; i < pargs.Length; i++)
            {
                if (pargs[i] is ITypeParameterSymbol hp && Same(hp.ContainingSymbol, type))
                {
                    if (hpos[hp.Ordinal] != -1)
                    {
                        Error(9, $"Stream handler {Name(type)} maps {hp.Name} to multiple request type arguments. Give each handler type parameter one position.", type);
                        return;
                    }
                    pmap[i] = i;
                    h2d[hp.Ordinal] = i;
                    hpos[hp.Ordinal] = i;
                }
                else if (!HasOpenArguments(pargs[i]) && Public(pargs[i]))
                {
                    pmap[i] = -1;
                    pfixed[i] = pargs[i];
                }
                else
                {
                    Error(9, $"Stream handler {Name(type)} has an unsupported pattern argument {Name(pargs[i])}. Use a handler type parameter or a concrete type.", type);
                    return;
                }
            }
            for (var j = 0; j < h2d.Length; j++)
            {
                if (h2d[j] < 0)
                {
                    Error(9, $"Stream handler {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                    return;
                }
            }
            if (!EnsureOpenStreamRequest(reqDef)) return;
            var itemPattern = contract.TypeArguments[1];
            if (!LeavesPublicOrParam(itemPattern, type))
            {
                Error(3, $"Stream handler {Name(type)} item {Name(itemPattern)} must be public or built from handler type parameters.", type);
                return;
            }
            if (MayBeRefLike(itemPattern, type))
            {
                Error(12, $"Stream handler {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                return;
            }
            var mapped = Substitute(openStreamRequests[reqDef], reqDef, pargs.ToArray(), compilation);
            if (!Same(mapped, itemPattern))
            {
                Error(3, $"Stream handler {Name(type)} item {Name(itemPattern)} does not match request {Name(pattern)} item {Name(mapped)}.", type);
                return;
            }
            if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
            {
                Error(12, $"Stream handler {Name(type)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", type);
                return;
            }
            var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
            var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
            for (var j = 0; j < type.TypeParameters.Length; j++)
            {
                if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
                {
                    Error(11, $"Stream handler {Name(type)} declares constraints that request {Name(reqDef)} does not satisfy. Loosen the handler or tighten the request.", type);
                    return;
                }
            }
            openStreamHandlers.Add(new OpenBinding(type, reqDef, itemPattern, pmap, pfixed, h2d, hpos, isVoid: false) { Merged = merged });
        }

        foreach (var type in allTypes)
        {
            ct.ThrowIfCancellationRequested();
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract) continue;
            var contracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
            if (contracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (type.IsRefLikeType)
                        Error(12, $"Request {Name(type)} is a ref struct. Ref struct requests require synchronous dispatch (SendSync).", type);
                    else if (contracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(contracts[0].TypeArguments[0], type))
                        Error(3, $"Request {Name(type)} must be a public generic definition with exactly one response contract over public or type-parameter types.", type);
                    else if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
                        Error(12, $"Request {Name(type)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", type);
                    else openRequests[type] = contracts[0].TypeArguments[0];
                }
                else if (type.IsRefLikeType)
                    Error(12, $"Request {Name(type)} is a ref struct. Ref struct requests require synchronous dispatch (SendSync).", type);
                else if (contracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(contracts[0].TypeArguments[0]))
                    Error(3, $"Request {Name(type)} must be public, non-generic and have exactly one public response contract.", type);
                else requests[type] = contracts[0].TypeArguments[0];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, handlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenHandler(type, contract, openHandlers, isVoid: false);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete request.", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (!handlers.TryGetValue(request, out var list)) handlers[request] = list = new();
                if (!list.Any(h => Same(h, type))) list.Add(type);
                // A handler may refer to contracts from a separate assembly; include its exact request.
                var responseContracts = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
                if (responseContracts.Length != 1 || !Public(request) || !Public(contract.TypeArguments[1]))
                {
                    Error(3, $"Invalid request contract on {Name(type)}.", type);
                }
                else if (!Same(responseContracts[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Handler {Name(type)} response {Name(contract.TypeArguments[1])} does not match request {Name(request)} response {Name(responseContracts[0].TypeArguments[0])}.", type);
                }
                else if (requests.TryGetValue(request, out var existing) && !Same(existing, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else requests[request] = contract.TypeArguments[1];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, voidHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenHandler(type, contract, openVoidHandlers, isVoid: true);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete void request.", type);
                    continue;
                }
                if (request.IsRefLikeType)
                {
                    Error(12, $"Handler {Name(type)} targets ref struct request {Name(request)}. Ref struct requests require synchronous dispatch (SendSync).", type);
                    continue;
                }
                if (!request.AllInterfaces.Any(i => Same(i.OriginalDefinition, voidRequestDefinition)))
                {
                    Error(3, $"Handler {Name(type)} request {Name(request)} must implement Zendiator.IRequest.", type);
                    continue;
                }
                if (!voidHandlers.TryGetValue(request, out var vlist)) voidHandlers[request] = vlist = new();
                if (!vlist.Any(h => Same(h, type))) vlist.Add(type);
                // Mirror the response-contract tracking so a void request reached only
                // through its handler still forms a route.
                var voidContracts = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
                if (voidContracts.Length != 1 || !Public(request) || !Public(voidContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Invalid request contract on {Name(type)}.", type);
                }
                else if (requests.TryGetValue(request, out var existing) && !Same(existing, voidContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else requests[request] = voidContracts[0].TypeArguments[0];
            }
            var syncContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncRequestDefinition)).ToArray();
            if (syncContracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (syncContracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(syncContracts[0].TypeArguments[0], type))
                        Error(3, $"Request {Name(type)} must be a public generic definition with exactly one response contract over public or type-parameter types.", type);
                    else if (MayBeRefLike(syncContracts[0].TypeArguments[0], type))
                        Error(12, $"Request {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                    else openSyncRequests[type] = syncContracts[0].TypeArguments[0];
                }
                else if (syncContracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(syncContracts[0].TypeArguments[0]))
                {
                    Error(3, $"Request {Name(type)} must be public, non-generic and have exactly one public response contract.", type);
                }
                else if (MayBeRefLike(syncContracts[0].TypeArguments[0], type))
                {
                    Error(12, $"Request {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                }
                else syncRequests[type] = syncContracts[0].TypeArguments[0];
            }
            if (type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    if (!IsPublicDefinition(type))
                        Error(3, $"Request {Name(type)} must be a public generic definition.", type);
                    else syncVoidRequests.Add(type);
                }
                else if (!Public(type))
                {
                    Error(3, $"Request {Name(type)} must be public.", type);
                }
                else syncVoidRequests.Add(type);
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSyncHandler(type, contract, openSyncHandlers, isVoid: false);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete sync request.", type);
                    continue;
                }
                var reqSync = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncRequestDefinition)).ToArray();
                if (reqSync.Length != 1 || !Public(request) || !Public(contract.TypeArguments[1]))
                {
                    if (reqSync.Length == 0 && request.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets void sync request {Name(request)}. Use one-argument ISyncRequestHandler<{Name(request)}>.", type);
                    else if (request.AllInterfaces.Any(i => Same(i.OriginalDefinition, requestDefinition) || Same(i.OriginalDefinition, voidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets async request {Name(request)}. Async requests require SendAsync; synchronous dispatch only accepts ISyncRequest.", type);
                    else
                        Error(3, $"Invalid sync request contract on {Name(type)}.", type);
                    continue;
                }
                if (MayBeRefLike(contract.TypeArguments[1], type))
                {
                    Error(12, $"Handler {Name(type)} has a ref-like response. Ref struct responses are not supported; use an ownable response type.", type);
                    continue;
                }
                if (!Same(reqSync[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Handler {Name(type)} response {Name(contract.TypeArguments[1])} does not match request {Name(request)} response {Name(reqSync[0].TypeArguments[0])}.", type);
                    continue;
                }
                if (!syncHandlers.TryGetValue(request, out var list)) syncHandlers[request] = list = new();
                if (!list.Any(h => Same(h, type))) list.Add(type);
                if (syncRequests.TryGetValue(request, out var existing) && !Same(existing, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting response contracts for {Name(request)}.", type);
                }
                else syncRequests[request] = contract.TypeArguments[1];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncVoidHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSyncHandler(type, contract, openSyncVoidHandlers, isVoid: true);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol request ||
                    request.IsAbstract || request.TypeKind == TypeKind.Interface || IsOpenDefinition(request) || HasOpenArguments(request))
                {
                    Error(3, $"Handler {Name(type)} must be an accessible non-generic class for a concrete void sync request.", type);
                    continue;
                }
                if (!request.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncVoidRequestDefinition)))
                {
                    if (request.AllInterfaces.Any(i => Same(i.OriginalDefinition, requestDefinition) || Same(i.OriginalDefinition, voidRequestDefinition)))
                        Error(8, $"Handler {Name(type)} targets async request {Name(request)}. Async requests require SendAsync; synchronous dispatch only accepts ISyncRequest.", type);
                    else
                        Error(8, $"Handler {Name(type)} targets response sync request {Name(request)}. Use two-argument ISyncRequestHandler<{Name(request)}, TResponse>.", type);
                    continue;
                }
                if (!Public(request))
                {
                    Error(3, $"Invalid sync request contract on {Name(type)}.", type);
                    continue;
                }
                if (!syncVoidHandlers.TryGetValue(request, out var vlist)) syncVoidHandlers[request] = vlist = new();
                if (!vlist.Any(h => Same(h, type))) vlist.Add(type);
                syncVoidRequests.Add(request);
            }
            var streamContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
            if (streamContracts.Length > 0)
            {
                if (IsOpenDefinition(type))
                {
                    if (type.IsRefLikeType)
                        Error(12, $"Stream request {Name(type)} is a ref struct. Ref struct stream requests are not supported.", type);
                    else if (streamContracts.Length != 1 || !IsPublicDefinition(type) || !LeavesPublicOrParam(streamContracts[0].TypeArguments[0], type))
                        Error(3, $"Stream request {Name(type)} must be a public generic definition with exactly one item contract over public or type-parameter types.", type);
                    else if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
                        Error(12, $"Stream request {Name(type)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", type);
                    else if (MayBeRefLike(streamContracts[0].TypeArguments[0], type))
                        Error(12, $"Stream request {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                    else openStreamRequests[type] = streamContracts[0].TypeArguments[0];
                }
                else if (type.IsRefLikeType)
                    Error(12, $"Stream request {Name(type)} is a ref struct. Ref struct stream requests are not supported.", type);
                else if (streamContracts.Length != 1 || HasParameters(type) || !Public(type) || !Public(streamContracts[0].TypeArguments[0]))
                    Error(3, $"Stream request {Name(type)} must be public, non-generic and have exactly one public item contract.", type);
                else if (streamContracts[0].TypeArguments[0] is INamedTypeSymbol streamItem && streamItem.IsRefLikeType)
                    Error(12, $"Stream request {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                else streamRequests[type] = streamContracts[0].TypeArguments[0];
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenStreamHandler(type, contract);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol streamReq ||
                    streamReq.IsAbstract || streamReq.TypeKind == TypeKind.Interface || IsOpenDefinition(streamReq) || HasOpenArguments(streamReq))
                {
                    Error(3, $"Stream handler {Name(type)} must be an accessible non-generic class for a concrete stream request.", type);
                    continue;
                }
                if (streamReq.IsRefLikeType)
                {
                    Error(12, $"Stream handler {Name(type)} targets ref struct request {Name(streamReq)}. Ref struct stream requests are not supported.", type);
                    continue;
                }
                if (contract.TypeArguments[1] is INamedTypeSymbol streamItemType && streamItemType.IsRefLikeType)
                {
                    Error(12, $"Stream handler {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
                    continue;
                }
                if (!streamHandlers.TryGetValue(streamReq, out var slist)) streamHandlers[streamReq] = slist = new();
                if (!slist.Any(h => Same(h, type))) slist.Add(type);
                var streamResponseContracts = streamReq.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
                if (streamResponseContracts.Length != 1 || !Public(streamReq) || !Public(contract.TypeArguments[1]))
                {
                    Error(3, $"Invalid stream request contract on {Name(type)}.", type);
                }
                else if (!Same(streamResponseContracts[0].TypeArguments[0], contract.TypeArguments[1]))
                {
                    Error(3, $"Stream handler {Name(type)} item {Name(contract.TypeArguments[1])} does not match request {Name(streamReq)} item {Name(streamResponseContracts[0].TypeArguments[0])}.", type);
                }
                else if (streamRequests.TryGetValue(streamReq, out var existingStream) && !Same(existingStream, contract.TypeArguments[1]))
                {
                    Error(3, $"Conflicting item contracts for {Name(streamReq)}.", type);
                }
                else streamRequests[streamReq] = contract.TypeArguments[1];
            }
            var notificationContracts = type.AllInterfaces.Where(i => Same(i.OriginalDefinition, notificationDefinition)).ToArray();
            if (notificationContracts.Length > 0 && !type.IsRefLikeType)
            {
                if (IsOpenDefinition(type))
                {
                    if (!IsPublicDefinition(type))
                        Error(3, $"Notification {Name(type)} must be a public generic definition.", type);
                    else openNotifications.Add(type);
                }
                else if (!Public(type))
                {
                    Error(3, $"Notification {Name(type)} must be public.", type);
                }
                else knownNotifications.Add(type);
            }
            foreach (var contract in type.AllInterfaces.Where(i => Same(i.OriginalDefinition, notificationHandlerDefinition)))
            {
                if (IsOpenDefinition(type))
                {
                    CollectOpenSubscriber(type, contract);
                    continue;
                }
                if (type.TypeKind != TypeKind.Class || HasParameters(type) ||
                    !compilation.IsSymbolAccessibleWithin(type, accessContext) ||
                    contract.TypeArguments[0] is not INamedTypeSymbol target ||
                    target.IsAbstract || target.TypeKind == TypeKind.Interface || IsOpenDefinition(target) || HasOpenArguments(target))
                {
                    Error(3, $"Subscriber {Name(type)} must be an accessible non-generic class for a concrete notification.", type);
                    continue;
                }
                if (target.IsRefLikeType)
                {
                    Error(12, $"Subscriber {Name(type)} targets ref struct notification {Name(target)}. Ref struct notifications are not supported.", type);
                    continue;
                }
                if (!target.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
                {
                    Error(13, $"Subscriber {Name(type)} target {Name(target)} must implement Zendiator.INotification.", type);
                    continue;
                }
                if (!Public(target))
                {
                    Error(13, $"Subscriber {Name(type)} target {Name(target)} must be public. Declare the notification or include its assembly.", type);
                    continue;
                }
                knownNotifications.Add(target);
                AddSubscriber(target, type);
            }
        }

        bool IsResponseMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, multiResponseDefinition));
        bool IsVoidMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, multiVoidDefinition));
        bool IsSyncResponseMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncMultiResponseDefinition));
        bool IsSyncVoidMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncMultiVoidDefinition));

        var routes = new List<Route>();
        foreach (var pair in requests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            var multiResp = IsResponseMulti(pair.Key);
            var multiVoid = IsVoidMulti(pair.Key);
            if (multiResp && multiVoid)
            {
                Error(8, $"Conflicting request kinds for {Name(pair.Key)}: response and void multi contracts cannot be combined. Keep one.", pair.Key);
                continue;
            }
            if (multiResp || multiVoid) continue;
            handlers.TryGetValue(pair.Key, out var list);
            voidHandlers.TryGetValue(pair.Key, out var vlist);
            var legacyCount = list?.Count ?? 0;
            var voidCount = vlist?.Count ?? 0;
            if (voidCount != 0 && legacyCount != 0)
            {
                Error(8, $"Conflicting void handlers for {Name(pair.Key)}: native-void and legacy Unit handlers cannot be combined. Keep one contract.", pair.Key);
            }
            else if (voidCount > 1)
            {
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", vlist!.Select(Name))}.", pair.Key);
            }
            else if (voidCount == 1)
            {
                routes.Add(new Route(pair.Key, pair.Value, vlist![0], isVoid: true));
            }
            else if (list == null || list.Count == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (list.Count != 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", list.Select(Name))}.", pair.Key);
            else routes.Add(new Route(pair.Key, pair.Value, list[0]));
        }

        void BuildOpenRoute(INamedTypeSymbol def, ITypeSymbol template, OpenBinding binding, bool isVoid, bool isSync, List<Route> target)
        {
            var varying = new List<int>();
            for (var i = 0; i < binding.PatternMap.Length; i++)
                if (binding.PatternMap[i] >= 0) varying.Add(i);
            var reqArgs = new string[binding.PatternMap.Length];
            for (var i = 0; i < reqArgs.Length; i++)
                reqArgs[i] = binding.PatternMap[i] >= 0 ? def.TypeParameters[i].Name : Name(binding.PatternFixed[i]);
            var reqDisplay = ClosedGenericName(def, reqArgs);
            string respDisplay = "";
            if (!isVoid)
            {
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
                respDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
            }
            var hArgs = new string[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
            var route = new Route(def, template, binding.Definition, isVoid: isVoid, isSync: isSync) { IsOpen = true };
            foreach (var vi in varying)
            {
                route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
                route.MethodConstraints.Add(RenderConstraints(binding.Merged[vi]));
            }
            route.RequestDisplay = reqDisplay;
            route.ResponseDisplay = respDisplay;
            route.HandlerDisplay = ClosedGenericName(binding.Definition, hArgs);
            route.HandlerContractDisplay = isSync
                ? (isVoid ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>" : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>")
                : (isVoid ? $"global::Zendiator.IRequestHandler<{reqDisplay}>" : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>");
            target.Add(route);
        }

        bool Covers(OpenBinding binding, INamedTypeSymbol closed)
        {
            if (!Same(closed.OriginalDefinition, binding.RequestDefinition)) return false;
            var cargs = closed.TypeArguments;
            if (cargs.Length != binding.PatternMap.Length) return false;
            for (var i = 0; i < binding.PatternMap.Length; i++)
            {
                if (binding.PatternMap[i] < 0 && !Same(binding.PatternFixed[i], cargs[i])) return false;
            }
            var hargs = new ITypeSymbol[binding.HandlerToDef.Length];
            for (var j = 0; j < hargs.Length; j++) hargs[j] = cargs[binding.HandlerPosition[j]];
            return SatisfiesConstraints(binding.Definition, hargs, compilation);
        }

        foreach (var def in openRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            if (IsResponseMulti(def) && IsVoidMulti(def))
            {
                Error(8, $"Conflicting request kinds for {Name(def)}: response and void multi contracts cannot be combined. Keep one.", def);
                continue;
            }
            if (IsResponseMulti(def) || IsVoidMulti(def)) continue;
            var template = openRequests[def];
            var binds = openHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            var vbinds = openVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count != 0 && vbinds.Count != 0)
            {
                Error(8, $"Conflicting generic handlers for {Name(def)}: native-void and response handlers cannot be combined. Keep one contract.", def);
            }
            else if (binds.Count > 1 || vbinds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single request supports one open handler. Use IMultiRequest for fan-out.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenRoute(def, template, binds[0], isVoid: false, isSync: false, routes);
            }
            else if (vbinds.Count == 1)
            {
                BuildOpenRoute(def, template, vbinds[0], isVoid: true, isSync: false, routes);
            }
            else
            {
                var hasClosed = handlers.Keys.Concat(voidHandlers.Keys)
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }

        foreach (var route in routes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openHandlers.Concat(openVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Same(binding.RequestDefinition, route.Request.OriginalDefinition) &&
                    (IsResponseMulti(binding.RequestDefinition) || IsVoidMulti(binding.RequestDefinition))) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }

        void BuildOpenStreamRoute(INamedTypeSymbol def, ITypeSymbol template, OpenBinding binding, List<Route> target)
        {
            var varying = new List<int>();
            for (var i = 0; i < binding.PatternMap.Length; i++)
                if (binding.PatternMap[i] >= 0) varying.Add(i);
            var reqArgs = new string[binding.PatternMap.Length];
            for (var i = 0; i < reqArgs.Length; i++)
                reqArgs[i] = binding.PatternMap[i] >= 0 ? def.TypeParameters[i].Name : Name(binding.PatternFixed[i]);
            var reqDisplay = ClosedGenericName(def, reqArgs);
            var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
            var itemDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
            var hArgs = new string[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
            var route = new Route(def, template, binding.Definition, isVoid: false, isSync: false) { IsOpen = true };
            foreach (var vi in varying)
            {
                route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
                route.MethodConstraints.Add(RenderConstraints(binding.Merged[vi]));
            }
            route.RequestDisplay = reqDisplay;
            route.ResponseDisplay = itemDisplay;
            route.HandlerDisplay = ClosedGenericName(binding.Definition, hArgs);
            route.HandlerContractDisplay = $"global::Zendiator.IStreamRequestHandler<{reqDisplay}, {itemDisplay}>";
            target.Add(route);
        }

        var streamRoutes = new List<Route>();
        foreach (var pair in streamRequests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            streamHandlers.TryGetValue(pair.Key, out var slist);
            var count = slist?.Count ?? 0;
            if (count == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (count != 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", slist!.Select(Name))}.", pair.Key);
            else streamRoutes.Add(new Route(pair.Key, pair.Value, slist![0]));
        }
        foreach (var def in openStreamRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var template = openStreamRequests[def];
            var binds = openStreamHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single stream request supports one open handler.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenStreamRoute(def, template, binds[0], streamRoutes);
            }
            else
            {
                var hasClosed = streamHandlers.Keys
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }
        foreach (var route in streamRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            foreach (var binding in openStreamHandlers)
            {
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }

        var multiRoutes = new List<MultiRoute>();

        void AddMultiBranch(MultiRoute route, INamedTypeSymbol handler, bool handlerIsOpen, string handlerDisplay, string handlerContractDisplay)
        {
            var branch = new MultiBranch
            {
                Handler = handler,
                HandlerIsOpen = handlerIsOpen,
                HandlerDisplay = handlerDisplay,
                HandlerContractDisplay = handlerContractDisplay,
                Order = SubscriberOrder(handler),
                AssemblyId = handler.ContainingAssembly.Identity.ToString(),
                FullName = Name(handler),
            };
            route.Branches.Add(branch);
        }

        void BuildClosedMulti(INamedTypeSymbol request, ITypeSymbol response, bool isVoid)
        {
            var route = new MultiRoute(request, response, isVoid, isOpen: false);
            var contractName = isVoid
                ? $"global::Zendiator.IRequestHandler<{Name(request)}>"
                : $"global::Zendiator.IRequestHandler<{Name(request)}, {Name(response)}>";
            if (isVoid)
            {
                if (handlers.TryGetValue(request, out var legacy) && legacy.Count != 0)
                {
                    Error(8, $"Multi request {Name(request)} requires one-argument handlers. Migrate legacy Unit handlers to IRequestHandler<{Name(request)}>.", request);
                    return;
                }
                if (!voidHandlers.TryGetValue(request, out var vlist) || vlist.Count == 0)
                {
                    Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                    return;
                }
                foreach (var handler in vlist)
                    AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
            }
            else
            {
                if (voidHandlers.TryGetValue(request, out var vlist) && vlist.Count != 0)
                {
                    Error(8, $"Multi request {Name(request)} requires two-argument handlers. Use IRequestHandler<{Name(request)}, {Name(response)}>.", request);
                    return;
                }
                if (!handlers.TryGetValue(request, out var list) || list.Count == 0)
                {
                    Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                    return;
                }
                foreach (var handler in list)
                    AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
            }
            // Union with covering open bindings: every applicable handler runs.
            var pool = isVoid ? openVoidHandlers : openHandlers;
            foreach (var binding in pool)
            {
                if (!Same(binding.RequestDefinition, request.OriginalDefinition) && !Same(binding.RequestDefinition, request)) continue;
                if (!Covers(binding, request)) continue;
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++)
                    hArgs[j] = Name(request.TypeArguments[binding.HandlerPosition[j]]);
                var display = ClosedGenericName(binding.Definition, hArgs);
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = request.TypeArguments[binding.HandlerPosition[j]];
                var respDisplay = isVoid ? "" : Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
                var reqDisplay = Name(request);
                var contractDisplay = isVoid
                    ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                    : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
                if (route.Branches.Any(b => Same(b.Handler, binding.Definition))) continue;
                AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
            }
            route.Branches.Sort(static (a, b) =>
            {
                var order = a.Order.CompareTo(b.Order);
                if (order != 0) return order;
                var assembly = string.Compare(a.AssemblyId, b.AssemblyId, StringComparison.Ordinal);
                return assembly != 0 ? assembly : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
            });
            multiRoutes.Add(route);
        }

        void BuildOpenMulti(INamedTypeSymbol def, ITypeSymbol template, List<OpenBinding> binds, bool isVoid)
        {
            var route = new MultiRoute(def, template, isVoid, isOpen: true);
            var reqArgs = new string[binds[0].PatternMap.Length];
            var varying = new List<int>();
            for (var i = 0; i < reqArgs.Length; i++)
            {
                if (binds.All(b => b.PatternMap[i] >= 0))
                {
                    varying.Add(i);
                    reqArgs[i] = def.TypeParameters[i].Name;
                }
                else if (binds.All(b => b.PatternMap[i] < 0 && Same(b.PatternFixed[i], binds[0].PatternFixed[i])))
                {
                    reqArgs[i] = Name(binds[0].PatternFixed[i]);
                }
                else
                {
                    Error(10, $"Ambiguous generic binding for {Name(def)}: open multi handlers disagree on type argument {i}. Align the patterns.", def);
                    return;
                }
            }
            var reqDisplay = ClosedGenericName(def, reqArgs);
            var merged = def.TypeParameters.Select(FromTypeParam).ToArray();
            var ordered = binds
                .Select(b => (Binding: b, Order: SubscriberOrder(b.Definition), Assembly: b.Definition.ContainingAssembly.Identity.ToString(), Full: Name(b.Definition)))
                .OrderBy(static x => x.Order)
                .ThenBy(static x => x.Assembly, StringComparer.Ordinal)
                .ThenBy(static x => x.Full, StringComparer.Ordinal)
                .Select(static x => x.Binding).ToList();
            foreach (var binding in ordered)
            {
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
                for (var j = 0; j < binding.Definition.TypeParameters.Length; j++)
                {
                    if (!MergeHandlerParam(merged[binding.HandlerToDef[j]], binding.Definition.TypeParameters[j], binding.Definition, mapArgs, compilation))
                    {
                        Error(11, $"Subscriber {Name(binding.Definition)} declares constraints that notification {Name(def)} does not satisfy.", binding.Definition);
                        return;
                    }
                }
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++)
                {
                    var pos = binding.HandlerPosition[j];
                    hArgs[j] = binding.PatternMap[pos] >= 0 ? def.TypeParameters[pos].Name : Name(binding.PatternFixed[pos]);
                }
                var display = ClosedGenericName(binding.Definition, hArgs);
                string respDisplay = "";
                if (!isVoid) respDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
                var contractDisplay = isVoid
                    ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                    : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
                AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
            }
            foreach (var vi in varying)
            {
                route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
                route.MethodConstraints.Add(RenderConstraints(merged[vi]));
            }
            route.RequestDisplay = reqDisplay;
            if (!isVoid)
            {
                var first = binds[0];
                var firstMap = new ITypeSymbol[first.Definition.TypeParameters.Length];
                for (var j = 0; j < firstMap.Length; j++) firstMap[j] = def.TypeParameters[first.HandlerToDef[j]];
                route.ResponseDisplay = Name(Substitute(first.ResponsePattern, first.Definition, firstMap, compilation));
            }
            multiRoutes.Add(route);
        }

        foreach (var pair in requests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            if (IsResponseMulti(pair.Key)) BuildClosedMulti(pair.Key, pair.Value, isVoid: false);
            else if (IsVoidMulti(pair.Key)) BuildClosedMulti(pair.Key, pair.Value, isVoid: true);
        }
        foreach (var def in openRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var isResp = IsResponseMulti(def);
            var isVoid = IsVoidMulti(def);
            if (!isResp && !isVoid) continue;
            var template = openRequests[def];
            if (isResp)
            {
                var binds = openHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = handlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenMulti(def, template, binds, isVoid: false);
            }
            else
            {
                var binds = openVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = voidHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenMulti(def, template, binds, isVoid: true);
            }
        }

        var syncRoutes = new List<Route>();
        var syncMultiRoutes = new List<MultiRoute>();
        foreach (var pair in syncRequests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            var mr = IsSyncResponseMulti(pair.Key);
            var mv = IsSyncVoidMulti(pair.Key);
            if (mr && mv)
            {
                Error(8, $"Conflicting request kinds for {Name(pair.Key)}: response and void sync multi contracts cannot be combined. Keep one.", pair.Key);
                continue;
            }
            if (mr)
            {
                BuildClosedSyncMulti(pair.Key, pair.Value, isVoid: false);
                continue;
            }
            if (mv)
            {
                BuildClosedSyncMulti(pair.Key, pair.Value, isVoid: true);
                continue;
            }
            syncHandlers.TryGetValue(pair.Key, out var list);
            syncVoidHandlers.TryGetValue(pair.Key, out var vlist);
            var rc = list?.Count ?? 0;
            var vc = vlist?.Count ?? 0;
            if (rc != 0 && vc != 0)
            {
                Error(8, $"Conflicting sync handlers for {Name(pair.Key)}: response and void handlers cannot be combined. Keep one contract.", pair.Key);
            }
            else if (vc > 1)
            {
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", vlist!.Select(Name))}.", pair.Key);
            }
            else if (vc == 1)
            {
                syncRoutes.Add(new Route(pair.Key, pair.Value, vlist![0], isVoid: true, isSync: true));
            }
            else if (rc == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (rc > 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", list!.Select(Name))}.", pair.Key);
            else syncRoutes.Add(new Route(pair.Key, pair.Value, list![0], isSync: true));
        }
        foreach (var req in syncVoidRequests.OrderBy(Name, StringComparer.Ordinal))
        {
            if (syncRequests.ContainsKey(req) || IsOpenDefinition(req)) continue;
            var mr = IsSyncResponseMulti(req);
            var mv = IsSyncVoidMulti(req);
            if (mr && mv)
            {
                Error(8, $"Conflicting request kinds for {Name(req)}: response and void sync multi contracts cannot be combined. Keep one.", req);
                continue;
            }
            if (mr)
            {
                Error(8, $"Multi request {Name(req)} must implement ISyncRequest<TResponse>.", req);
                continue;
            }
            if (mv)
            {
                BuildClosedSyncMulti(req, compilation.GetSpecialType(SpecialType.System_Void), isVoid: true);
                continue;
            }
            syncVoidHandlers.TryGetValue(req, out var vlist);
            syncHandlers.TryGetValue(req, out var list);
            var vc = vlist?.Count ?? 0;
            var rc = list?.Count ?? 0;
            if (rc != 0)
            {
                Error(8, $"Handler {Name(list![0])} targets void sync request {Name(req)}. Use one-argument ISyncRequestHandler<{Name(req)}>.", req);
            }
            else if (vc == 0)
                Error(1, $"No handler found for {Name(req)}. Include its assembly.", req);
            else if (vc > 1)
                Error(2, $"Multiple handlers found for {Name(req)}: {string.Join(", ", vlist!.Select(Name))}.", req);
            else syncRoutes.Add(new Route(req, compilation.GetSpecialType(SpecialType.System_Void), vlist![0], isVoid: true, isSync: true));
        }
        foreach (var def in openSyncRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            if (IsSyncResponseMulti(def) && IsSyncVoidMulti(def))
            {
                Error(8, $"Conflicting request kinds for {Name(def)}: response and void sync multi contracts cannot be combined. Keep one.", def);
                continue;
            }
            if (IsSyncResponseMulti(def) || IsSyncVoidMulti(def)) continue;
            var template = openSyncRequests[def];
            var binds = openSyncHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            var vbinds = openSyncVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count != 0 && vbinds.Count != 0)
            {
                Error(8, $"Conflicting generic handlers for {Name(def)}: native-void and response handlers cannot be combined. Keep one contract.", def);
            }
            else if (binds.Count > 1 || vbinds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single request supports one open handler. Use ISyncMultiRequest for fan-out.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenRoute(def, template, binds[0], isVoid: false, isSync: true, syncRoutes);
            }
            else if (vbinds.Count == 1)
            {
                BuildOpenRoute(def, template, vbinds[0], isVoid: true, isSync: true, syncRoutes);
            }
            else
            {
                var hasClosed = syncHandlers.Keys.Concat(syncVoidHandlers.Keys)
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }
        foreach (var route in syncRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openSyncHandlers.Concat(openSyncVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }

        foreach (var route in syncRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openSyncHandlers.Concat(openSyncVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }

        void BuildClosedSyncMulti(INamedTypeSymbol request, ITypeSymbol response, bool isVoid)
        {
            var route = new MultiRoute(request, response, isVoid, isOpen: false, isSync: true);
            var contractName = isVoid
                ? $"global::Zendiator.ISyncRequestHandler<{Name(request)}>"
                : $"global::Zendiator.ISyncRequestHandler<{Name(request)}, {Name(response)}>";
            if (isVoid)
            {
                if (syncHandlers.TryGetValue(request, out var legacy) && legacy.Count != 0)
                {
                    Error(8, $"Multi request {Name(request)} requires one-argument sync handlers. Use ISyncRequestHandler<{Name(request)}>.", request);
                    return;
                }
                if (!syncVoidHandlers.TryGetValue(request, out var vlist) || vlist.Count == 0)
                {
                    Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                    return;
                }
                foreach (var handler in vlist)
                    AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
            }
            else
            {
                if (syncVoidHandlers.TryGetValue(request, out var vlist) && vlist.Count != 0)
                {
                    Error(8, $"Multi request {Name(request)} requires two-argument sync handlers. Use ISyncRequestHandler<{Name(request)}, {Name(response)}>.", request);
                    return;
                }
                if (!syncHandlers.TryGetValue(request, out var list) || list.Count == 0)
                {
                    Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                    return;
                }
                foreach (var handler in list)
                    AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
            }
            var pool = isVoid ? openSyncVoidHandlers : openSyncHandlers;
            foreach (var binding in pool)
            {
                if (!Same(binding.RequestDefinition, request.OriginalDefinition) && !Same(binding.RequestDefinition, request)) continue;
                if (!Covers(binding, request)) continue;
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++)
                    hArgs[j] = Name(request.TypeArguments[binding.HandlerPosition[j]]);
                var display = ClosedGenericName(binding.Definition, hArgs);
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = request.TypeArguments[binding.HandlerPosition[j]];
                var respDisplay = isVoid ? "" : Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
                var reqDisplay = Name(request);
                var contractDisplay = isVoid
                    ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>"
                    : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>";
                if (route.Branches.Any(b => Same(b.Handler, binding.Definition))) continue;
                AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
            }
            route.Branches.Sort(static (a, b) =>
            {
                var order = a.Order.CompareTo(b.Order);
                if (order != 0) return order;
                var assembly = string.Compare(a.AssemblyId, b.AssemblyId, StringComparison.Ordinal);
                return assembly != 0 ? assembly : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
            });
            syncMultiRoutes.Add(route);
        }

        void BuildOpenSyncMulti(INamedTypeSymbol def, ITypeSymbol template, List<OpenBinding> binds, bool isVoid)
        {
            var route = new MultiRoute(def, template, isVoid, isOpen: true, isSync: true);
            var reqArgs = new string[binds[0].PatternMap.Length];
            var varying = new List<int>();
            for (var i = 0; i < reqArgs.Length; i++)
            {
                if (binds.All(b => b.PatternMap[i] >= 0))
                {
                    varying.Add(i);
                    reqArgs[i] = def.TypeParameters[i].Name;
                }
                else if (binds.All(b => b.PatternMap[i] < 0 && Same(b.PatternFixed[i], binds[0].PatternFixed[i])))
                {
                    reqArgs[i] = Name(binds[0].PatternFixed[i]);
                }
                else
                {
                    Error(10, $"Ambiguous generic binding for {Name(def)}: open multi handlers disagree on type argument {i}. Align the patterns.", def);
                    return;
                }
            }
            var reqDisplay = ClosedGenericName(def, reqArgs);
            var merged = def.TypeParameters.Select(FromTypeParam).ToArray();
            for (var i = 0; i < merged.Length; i++) merged[i].AllowsRefLike = def.TypeParameters[i].AllowsRefLikeType;
            var ordered = binds
                .Select(b => (Binding: b, Order: SubscriberOrder(b.Definition), Assembly: b.Definition.ContainingAssembly.Identity.ToString(), Full: Name(b.Definition)))
                .OrderBy(static x => x.Order)
                .ThenBy(static x => x.Assembly, StringComparer.Ordinal)
                .ThenBy(static x => x.Full, StringComparer.Ordinal)
                .Select(static x => x.Binding).ToList();
            foreach (var binding in ordered)
            {
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
                for (var j = 0; j < binding.Definition.TypeParameters.Length; j++)
                {
                    if (!MergeHandlerParam(merged[binding.HandlerToDef[j]], binding.Definition.TypeParameters[j], binding.Definition, mapArgs, compilation))
                    {
                        Error(11, $"Handler {Name(binding.Definition)} declares constraints that request {Name(def)} does not satisfy.", binding.Definition);
                        return;
                    }
                }
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++)
                {
                    var pos = binding.HandlerPosition[j];
                    hArgs[j] = binding.PatternMap[pos] >= 0 ? def.TypeParameters[pos].Name : Name(binding.PatternFixed[pos]);
                }
                var display = ClosedGenericName(binding.Definition, hArgs);
                string respDisplay = "";
                if (!isVoid) respDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
                var contractDisplay = isVoid
                    ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>"
                    : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>";
                AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
            }
            foreach (var vi in varying)
            {
                route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
                route.MethodConstraints.Add(RenderConstraints(merged[vi]));
            }
            route.RequestDisplay = reqDisplay;
            if (!isVoid)
            {
                var first = binds[0];
                var firstMap = new ITypeSymbol[first.Definition.TypeParameters.Length];
                for (var j = 0; j < firstMap.Length; j++) firstMap[j] = def.TypeParameters[first.HandlerToDef[j]];
                route.ResponseDisplay = Name(Substitute(first.ResponsePattern, first.Definition, firstMap, compilation));
            }
            syncMultiRoutes.Add(route);
        }

        foreach (var def in openSyncRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var isResp = IsSyncResponseMulti(def);
            var isVoid = IsSyncVoidMulti(def);
            if (!isResp && !isVoid) continue;
            var template = openSyncRequests[def];
            if (isResp)
            {
                var binds = openSyncHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = syncHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenSyncMulti(def, template, binds, isVoid: false);
            }
            else
            {
                var binds = openSyncVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = syncVoidHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenSyncMulti(def, template, binds, isVoid: true);
            }
        }

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "Zendiator.NotificationAttribute") continue;
            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol declared)
            {
                Error(13, "Notification requires a notification type.");
                continue;
            }
            if (declared.IsRefLikeType)
            {
                Error(12, $"Notification {Name(declared)} is a ref struct. Ref struct notifications are not supported.", declared);
                continue;
            }
            if (!declared.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
            {
                Error(13, $"Notification {Name(declared)} must implement Zendiator.INotification.", declared);
                continue;
            }
            if (IsOpenDefinition(declared))
            {
                if (!IsPublicDefinition(declared))
                    Error(13, $"Notification {Name(declared)} must be public.", declared);
                else openNotifications.Add(declared);
            }
            else if (declared.IsAbstract || declared.TypeKind == TypeKind.Interface || HasOpenArguments(declared) || !Public(declared))
            {
                Error(13, $"Notification {Name(declared)} must be a public concrete type. Declare the notification or include its assembly.", declared);
            }
            else knownNotifications.Add(declared);
        }
        if (diMode)
        {
            foreach (var notification in diNotifications) knownNotifications.Add(notification);
            foreach (var def in diOpenNotifications) openNotifications.Add(def);
        }

        var notifications = new List<NotificationRoute>();
        foreach (var notification in knownNotifications.OrderBy(Name, StringComparer.Ordinal))
        {
            var route = new NotificationRoute(notification, isOpen: false);
            if (subscribers.TryGetValue(notification, out var subs))
            {
                route.Subscribers.AddRange(subs
                    .OrderBy(static s => s.Order)
                    .ThenBy(static s => s.AssemblyId, StringComparer.Ordinal)
                    .ThenBy(static s => s.FullName, StringComparer.Ordinal));
            }
            route.NotificationDisplay = Name(notification);
            notifications.Add(route);
        }
        foreach (var def in openNotifications.OrderBy(Name, StringComparer.Ordinal))
        {
            var route = new NotificationRoute(def, isOpen: true);
            var binds = openSubscribers.Where(b => Same(b.RequestDefinition, def))
                .Select(b => (Binding: b, Order: SubscriberOrder(b.Definition), Assembly: b.Definition.ContainingAssembly.Identity.ToString(), Full: Name(b.Definition)))
                .OrderBy(static x => x.Order)
                .ThenBy(static x => x.Assembly, StringComparer.Ordinal)
                .ThenBy(static x => x.Full, StringComparer.Ordinal)
                .Select(static x => x.Binding).ToList();
            var merged = def.TypeParameters.Select(FromTypeParam).ToArray();
            foreach (var binding in binds)
            {
                var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
                for (var j = 0; j < binding.Definition.TypeParameters.Length; j++)
                    MergeHandlerParam(merged[binding.HandlerToDef[j]], binding.Definition.TypeParameters[j], binding.Definition, mapArgs, compilation);
                var hArgs = new string[binding.Definition.TypeParameters.Length];
                for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
                route.Subscribers.Add(new Subscriber(binding.Definition, SubscriberOrder(binding.Definition), binding.Definition.ContainingAssembly.Identity.ToString(), Name(binding.Definition)));
                route.HandlerDisplays.Add(ClosedGenericName(binding.Definition, hArgs));
                route.HandlerContractDisplays.Add($"global::Zendiator.INotificationHandler<{Name(def)}>");
            }
            foreach (var p in def.TypeParameters) route.OpenTypeParams.Add(p.Name);
            foreach (var m in merged) route.MethodConstraints.Add(RenderConstraints(m));
            route.NotificationDisplay = Name(def);
            notifications.Add(route);
        }
        foreach (var route in notifications)
        {
            if (route.IsOpen || !route.Notification.IsGenericType) continue;
            foreach (var binding in openSubscribers)
            {
                if (Covers(binding, route.Notification))
                {
                    var closedName = route.Subscribers.Count == 0 ? "(none)" : Name(route.Subscribers[0].Handler);
                    Error(10, $"Ambiguous generic binding for {Name(route.Notification)}: closed subscriber {closedName} overlaps open subscriber {Name(binding.Definition)}. Keep one.", route.Notification);
                }
            }
        }

        foreach (var pipeline in pipelines.OrderBy(p => p.Order))
        {
            ct.ThrowIfCancellationRequested();
            var type = pipeline.Type;
            var isOpenBehavior = pipeline.IsOpenBehavior;
            var definition = isOpenBehavior ? type.OriginalDefinition : type;
            var contracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, behaviorDefinition)).ToArray();
            var voidContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, voidBehaviorDefinition)).ToArray();
            var syncContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncBehaviorDefinition)).ToArray();
            var syncVoidContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, syncVoidBehaviorDefinition)).ToArray();
            var streamContracts = definition.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamBehaviorDefinition)).ToArray();
            var accessible = compilation.IsSymbolAccessibleWithin(definition, accessContext);
            var validAsync = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && contracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(contracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(contracts[0].TypeArguments[1], definition.TypeParameters[1])));
            var validVoid = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && voidContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 1 && !HasGenericContainer(definition) &&
                     Same(voidContracts[0].TypeArguments[0], definition.TypeParameters[0])));
            var validSyncResponse = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && syncContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(syncContracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(syncContracts[0].TypeArguments[1], definition.TypeParameters[1])));
            var validSyncVoid = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && syncVoidContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 1 && !HasGenericContainer(definition) &&
                     Same(syncVoidContracts[0].TypeArguments[0], definition.TypeParameters[0])));
            var validStream = definition.TypeKind == TypeKind.Class && !definition.IsAbstract && accessible && streamContracts.Length == 1 &&
                (!isOpenBehavior ||
                 (definition.Arity == 2 && !HasGenericContainer(definition) &&
                     Same(streamContracts[0].TypeArguments[0], definition.TypeParameters[0]) &&
                     Same(streamContracts[0].TypeArguments[1], definition.TypeParameters[1])));
            if (!validAsync && !validVoid && !validSyncResponse && !validSyncVoid && !validStream)
            {
                Error(4, $"Invalid behavior {Name(type)}. Use an accessible class implementing one pipeline contract; open behaviors must map <TRequest,TResponse> directly and open void behaviors <TRequest> directly.", mediator);
                continue;
            }
            var matched = false;
            var matchedVoid = false;
            foreach (var route in routes)
            {
                if (route.IsVoid) continue;
                INamedTypeSymbol closed;
                if (isOpenBehavior)
                {
                    if (!validAsync) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        matched = true;
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                    closed = definition.Construct(route.Request, route.Response);
                }
                else
                {
                    if (!validAsync || route.IsOpen) continue;
                    if (!Same(contracts[0].TypeArguments[0], route.Request) || !Same(contracts[0].TypeArguments[1], route.Response)) continue;
                    closed = type;
                }
                route.Behaviors.Add(closed);
                matched = true;
            }
            foreach (var route in routes)
            {
                if (!route.IsVoid) continue;
                INamedTypeSymbol closed;
                if (isOpenBehavior)
                {
                    if (!validVoid) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}>");
                        matchedVoid = true;
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                    closed = definition.Construct(route.Request);
                }
                else
                {
                    if (!validVoid || route.IsOpen) continue;
                    if (!Same(voidContracts[0].TypeArguments[0], route.Request)) continue;
                    closed = type;
                }
                route.Behaviors.Add(closed);
                matchedVoid = true;
            }
            var matchedMulti = false;
            foreach (var route in multiRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validVoid) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}>");
                            matchedMulti = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request));
                            matchedMulti = true;
                        }
                        else
                        {
                            if (!Same(voidContracts[0].TypeArguments[0], route.Request)) continue;
                            branch.Behaviors.Add(type);
                            matchedMulti = true;
                        }
                    }
                }
                else
                {
                    if (!validAsync) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.IPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                            matchedMulti = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request, route.Response));
                            matchedMulti = true;
                        }
                        else
                        {
                            if (!Same(contracts[0].TypeArguments[0], route.Request) || !Same(contracts[0].TypeArguments[1], route.Response)) continue;
                            branch.Behaviors.Add(type);
                            matchedMulti = true;
                        }
                    }
                }
            }
            var matchedSync = false;
            foreach (var route in syncRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validSyncVoid) continue;
                    if (route.IsOpen)
                    {
                        if (!isOpenBehavior) continue;
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}>");
                        matchedSync = true;
                    }
                    else if (isOpenBehavior)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition.Construct(route.Request));
                        matchedSync = true;
                    }
                    else
                    {
                        if (!Same(syncVoidContracts[0].TypeArguments[0], route.Request)) continue;
                        route.Behaviors.Add(type);
                        matchedSync = true;
                    }
                }
                else
                {
                    if (!validSyncResponse) continue;
                    if (route.IsOpen)
                    {
                        if (!isOpenBehavior) continue;
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        matchedSync = true;
                    }
                    else if (isOpenBehavior)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                        route.Behaviors.Add(definition.Construct(route.Request, route.Response));
                        matchedSync = true;
                    }
                    else
                    {
                        if (!Same(syncContracts[0].TypeArguments[0], route.Request) || !Same(syncContracts[0].TypeArguments[1], route.Response)) continue;
                        route.Behaviors.Add(type);
                        matchedSync = true;
                    }
                }
            }
            foreach (var route in syncMultiRoutes)
            {
                if (route.IsVoid)
                {
                    if (!validSyncVoid) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}>");
                            matchedSync = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request));
                            matchedSync = true;
                        }
                        else
                        {
                            if (!Same(syncVoidContracts[0].TypeArguments[0], route.Request)) continue;
                            branch.Behaviors.Add(type);
                            matchedSync = true;
                        }
                    }
                }
                else
                {
                    if (!validSyncResponse) continue;
                    foreach (var branch in route.Branches)
                    {
                        if (route.IsOpen)
                        {
                            if (!isOpenBehavior) continue;
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition);
                            branch.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                            branch.BehaviorContractDisplays.Add($"global::Zendiator.ISyncPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                            matchedSync = true;
                        }
                        else if (isOpenBehavior)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                            if (route.Request.IsRefLikeType && !definition.TypeParameters[0].AllowsRefLikeType) continue;
                            branch.Behaviors.Add(definition.Construct(route.Request, route.Response));
                            matchedSync = true;
                        }
                        else
                        {
                            if (!Same(syncContracts[0].TypeArguments[0], route.Request) || !Same(syncContracts[0].TypeArguments[1], route.Response)) continue;
                            branch.Behaviors.Add(type);
                            matchedSync = true;
                        }
                    }
                }
            }
            if (!matched && !matchedVoid && !matchedMulti && !matchedSync && !isOpenBehavior)
            {
                // Stream-only behaviors are valid even when no regular route matches; check streams first.
                var streamMatched = false;
                foreach (var route in streamRoutes)
                {
                    if (isOpenBehavior)
                    {
                        if (!validStream) continue;
                        if (route.IsOpen)
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        }
                        else
                        {
                            if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        }
                        streamMatched = true;
                        break;
                    }
                    else
                    {
                        if (!validStream || route.IsOpen) continue;
                        if (!Same(streamContracts[0].TypeArguments[0], route.Request) || !Same(streamContracts[0].TypeArguments[1], route.Response)) continue;
                        streamMatched = true;
                        break;
                    }
                }
                if (!streamMatched)
                    Error(4, $"Behavior {Name(type)} does not match any registered request.", mediator);
            }
            // Attach stream behaviors to stream routes (typed, per-enumeration resolution).
            foreach (var route in streamRoutes)
            {
                INamedTypeSymbol closedStream;
                if (isOpenBehavior)
                {
                    if (!validStream) continue;
                    if (route.IsOpen)
                    {
                        if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                        route.Behaviors.Add(definition);
                        route.BehaviorDisplays.Add(ClosedGenericName(definition, new[] { route.RequestDisplay, route.ResponseDisplay }));
                        route.BehaviorContractDisplays.Add($"global::Zendiator.IStreamPipelineBehavior<{route.RequestDisplay}, {route.ResponseDisplay}>");
                        continue;
                    }
                    if (!SatisfiesConstraints(definition, new[] { route.Request, route.Response }, compilation)) continue;
                    closedStream = definition.Construct(route.Request, route.Response);
                }
                else
                {
                    if (!validStream || route.IsOpen) continue;
                    if (!Same(streamContracts[0].TypeArguments[0], route.Request) || !Same(streamContracts[0].TypeArguments[1], route.Response)) continue;
                    closedStream = type;
                }
                route.Behaviors.Add(closedStream);
            }
        }

        if (errors.Count != 0) return new Result("", "", errors);
        if (diMode)
        {
            var options = new DiEmitOptions { EmitExtensions = false, Fingerprint = diFingerprint };
            var mediatorSource = EmitAssembly(diNamespace!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, options);
            return new Result(mediatorSource, EmitInterceptorFile(diCallSites, "global::" + diNamespace! + "."), errors);
        }
        return assemblyMode
            ? new Result(EmitAssembly(assemblyNamespace!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, null), "", errors)
            : new Result(Emit(mediator!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition), "", errors);
    }

    private static string? ResolveAssemblyNamespace(AttributeData assemblyGen, Compilation compilation, List<Diagnostic> errors)
    {
        void Error(string message) => errors.Add(Diagnostic.Create(Rules[6], Location.None, message));
        string? ns = null;
        foreach (var pair in assemblyGen.NamedArguments)
        {
            if (pair.Key == "Namespace" && pair.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                ns = s.Trim();
        }
        ns ??= DefaultGeneratedNamespace(compilation.AssemblyName);
        if (ns == null || !IsValidNamespace(ns))
        {
            Error($"Invalid generation namespace '{ns ?? "<empty>"}'. Set [assembly: GenerateZendiator(Namespace = \"MyApp.Generated\")] with dot-separated identifiers.");
            return null;
        }
        foreach (var name in new[] { "Zendiator", "IZendiator", "ZendiatorServiceCollectionExtensions" })
        {
            if (compilation.GetTypeByMetadataName(ns + "." + name) != null)
            {
                Error($"Generated name collision: '{ns}.{name}' already exists. Choose a different Namespace.");
                return null;
            }
        }
        return ns;
    }

    private static string? DefaultGeneratedNamespace(string? assemblyName)
    {
        var name = assemblyName;
        if (name is not { Length: > 0 } || string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Split('.').Select(static part =>
        {
            var chars = part.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            var text = chars.Length == 0 ? "_" : new string(chars);
            return text.Length != 0 && (char.IsLetter(text[0]) || text[0] == '_') ? text : "_" + text;
        }).ToArray();
        return string.Join(".", clean) + ".Generated";
    }

    private static bool IsValidNamespace(string ns)
    {
        var parts = ns.Split('.');
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || !SyntaxFacts.IsValidIdentifier(part)) return false;
        }
        return true;
    }

    private static bool SatisfiesConstraints(INamedTypeSymbol type, ITypeSymbol[] args, Compilation compilation)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var parameter = type.TypeParameters[i];
            var arg = args[i];
            var nullableValue = arg is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if ((parameter.HasReferenceTypeConstraint && !arg.IsReferenceType) ||
                (parameter.HasValueTypeConstraint && (!arg.IsValueType || nullableValue)) ||
                (parameter.HasUnmanagedTypeConstraint && !arg.IsUnmanagedType) ||
                (parameter.HasNotNullConstraint && (arg.NullableAnnotation == NullableAnnotation.Annotated || nullableValue)) ||
                (parameter.HasReferenceTypeConstraint && parameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated && arg.NullableAnnotation == NullableAnnotation.Annotated))
                return false;
            if (parameter.HasConstructorConstraint && !arg.IsValueType &&
                arg is not ITypeParameterSymbol { HasConstructorConstraint: true } &&
                (arg is not INamedTypeSymbol named || named.IsAbstract ||
                 !named.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0)))
                return false;
            foreach (var constraint in parameter.ConstraintTypes)
            {
                var resolved = Substitute(constraint, type, args, compilation);
                if (arg is INamedTypeSymbol refArg && refArg.IsRefLikeType)
                {
                    // No boxing conversion exists for ref structs; verify the
                    // interface implementation directly instead.
                    if (resolved is INamedTypeSymbol required &&
                        refArg.AllInterfaces.Any(i => Same(i, required))) continue;
                    return false;
                }
                var conversion = ((CSharpCompilation)compilation).ClassifyConversion(arg, resolved);
                if (!(conversion.IsIdentity || conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing))) return false;
            }
        }
        return true;
    }

    private static ITypeSymbol Substitute(ITypeSymbol symbol, INamedTypeSymbol owner, ITypeSymbol[] args, Compilation compilation)
    {
        if (symbol is ITypeParameterSymbol p && Same(p.ContainingSymbol, owner)) return args[p.Ordinal];
        if (symbol is IArrayTypeSymbol array) return compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, owner, args, compilation), array.Rank);
        if (symbol is INamedTypeSymbol named && named.IsGenericType)
            return named.OriginalDefinition.Construct(named.TypeArguments.Select(t => Substitute(t, owner, args, compilation)).ToArray());
        return symbol;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var member in ns.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol child)
                foreach (var type in Types(child, ct)) yield return type;
            else if (member is INamedTypeSymbol named)
                foreach (var type in Types(named, ct)) yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamedTypeSymbol named, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return named;
        foreach (var nested in named.GetTypeMembers())
            foreach (var type in Types(nested, ct)) yield return type;
    }

    private static bool HasGenericContainer(INamedTypeSymbol type) => type.ContainingType != null && HasParameters(type.ContainingType);
    private static bool HasParameters(INamedTypeSymbol type) => type.Arity != 0 || HasGenericContainer(type);
    private static bool Public(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => Public(a.ElementType),
        INamedTypeSymbol n => n.DeclaredAccessibility == Accessibility.Public &&
            (n.ContainingType == null || Public(n.ContainingType)) && n.TypeArguments.All(Public),
        IDynamicTypeSymbol => true,
        _ => false
    };
    private static bool Same(ISymbol? a, ISymbol? b) => SymbolEqualityComparer.Default.Equals(a, b);
    private static string Name(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    private static bool IsOpenDefinition(INamedTypeSymbol type) =>
        type.IsUnboundGenericType ||
        (type.IsGenericType && type.TypeParameters.Length != 0 && Same(type.OriginalDefinition, type));

    private static bool HasOpenArguments(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol a => HasOpenArguments(a.ElementType),
        INamedTypeSymbol n => n.TypeArguments.Any(HasOpenArguments),
        _ => false
    };

    private static bool IsPublicDefinition(INamedTypeSymbol type)
    {
        for (var t = type; t != null; t = t.ContainingType)
            if (t.DeclaredAccessibility != Accessibility.Public) return false;
        return true;
    }

    private static bool LeavesPublicOrParam(ITypeSymbol type, INamedTypeSymbol owner)
    {
        if (type is IDynamicTypeSymbol) return true;
        if (type is ITypeParameterSymbol p) return Same(p.ContainingSymbol, owner);
        if (type is IArrayTypeSymbol a) return LeavesPublicOrParam(a.ElementType, owner);
        if (type is INamedTypeSymbol n)
        {
            if (!IsPublicDefinition(n.OriginalDefinition)) return false;
            return n.TypeArguments.All(t => LeavesPublicOrParam(t, owner));
        }
        return false;
    }

    private sealed class MergedParam(string name)
    {
        public string Name { get; } = name;
        public bool Class;
        public bool ClassNullable = true;
        public bool Struct;
        public bool Unmanaged;
        public bool NotNull;
        public bool New;
        public bool HasPrimary;
        public bool AllowsRefLike;
        public List<ITypeSymbol> Types = new();
    }

    private static MergedParam FromTypeParam(ITypeParameterSymbol p)
    {
        var m = new MergedParam(p.Name)
        {
            Class = p.HasReferenceTypeConstraint,
            ClassNullable = p.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated,
            Struct = p.HasValueTypeConstraint,
            Unmanaged = p.HasUnmanagedTypeConstraint,
            NotNull = p.HasNotNullConstraint,
            New = p.HasConstructorConstraint,
            HasPrimary = p.HasReferenceTypeConstraint || p.HasValueTypeConstraint || p.HasUnmanagedTypeConstraint,
            AllowsRefLike = p.AllowsRefLikeType,
        };
        m.Types.AddRange(p.ConstraintTypes);
        return m;
    }

    private static string PrimaryOf(MergedParam m) => m.Class ? "class" : m.Unmanaged ? "unmanaged" : m.Struct ? "struct" : "";

    private static bool MergeHandlerParam(MergedParam target, ITypeParameterSymbol source, INamedTypeSymbol handlerDef, ITypeSymbol[] mapArgs, Compilation compilation)
    {
        var sourcePrimary = source.HasReferenceTypeConstraint ? "class" : source.HasUnmanagedTypeConstraint ? "unmanaged" : source.HasValueTypeConstraint ? "struct" : "";
        var targetPrimary = PrimaryOf(target);
        if (sourcePrimary.Length != 0 && targetPrimary.Length != 0)
        {
            var compatible = sourcePrimary == targetPrimary ||
                (sourcePrimary == "unmanaged" && targetPrimary == "struct") ||
                (sourcePrimary == "struct" && targetPrimary == "unmanaged");
            if (!compatible) return false;
            if (targetPrimary == "struct" && sourcePrimary == "unmanaged") target.Unmanaged = true;
        }
        else if (sourcePrimary.Length != 0)
        {
            target.Class = source.HasReferenceTypeConstraint;
            target.Struct = source.HasValueTypeConstraint;
            target.Unmanaged = source.HasUnmanagedTypeConstraint;
            target.HasPrimary = true;
        }
        if (source.HasReferenceTypeConstraint)
            target.ClassNullable = target.ClassNullable && source.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated;
        target.NotNull = target.NotNull || source.HasNotNullConstraint;
        target.New = target.New || source.HasConstructorConstraint;
        foreach (var constraint in source.ConstraintTypes)
        {
            var mapped = Substitute(constraint, handlerDef, mapArgs, compilation);
            if (!target.Types.Any(t => Same(t, mapped))) target.Types.Add(mapped);
        }
        return true;
    }

    private static string RenderConstraints(MergedParam m)
    {
        var parts = new List<string>();
        if (m.Class) parts.Add(m.ClassNullable ? "class?" : "class");
        else if (m.Unmanaged) parts.Add("unmanaged");
        else if (m.Struct) parts.Add("struct");
        if (m.NotNull && !m.Class && !m.Struct && !m.Unmanaged) parts.Add("notnull");
        foreach (var t in m.Types) parts.Add(Name(t));
        if (m.New && !m.Struct && !m.Unmanaged) parts.Add("new()");
        if (m.AllowsRefLike) parts.Add("allows ref struct");
        if (parts.Count == 0) return "";
        return $" where {m.Name} : {string.Join(", ", parts)}";
    }

    private static string QualifiedName(INamedTypeSymbol def)
    {
        var chain = new List<string>();
        for (var t = def; t != null; t = t.ContainingType) chain.Add(t.Name);
        chain.Reverse();
        var ns = def.ContainingNamespace.IsGlobalNamespace ? null : def.ContainingNamespace.ToDisplayString();
        return (ns == null ? "global::" : "global::" + ns + ".") + string.Join(".", chain);
    }

    private static string ClosedGenericName(INamedTypeSymbol def, string[] ownArgs)
    {
        var name = QualifiedName(def);
        return ownArgs.Length == 0 ? name : name + "<" + string.Join(", ", ownArgs) + ">";
    }

    private static string OpenTypeofName(INamedTypeSymbol def) =>
        QualifiedName(def) + "<" + new string(',', def.Arity - 1) + ">";

    private sealed class DiEmitOptions
    {
        public bool EmitExtensions = true;
        public string Fingerprint = "";
    }

    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static bool MayBeRefLike(ITypeSymbol type, INamedTypeSymbol owner) => type switch
    {
        INamedTypeSymbol n when n.IsRefLikeType => true,
        ITypeParameterSymbol p => Same(p.ContainingSymbol, owner) && p.AllowsRefLikeType,
        IArrayTypeSymbol a => MayBeRefLike(a.ElementType, owner),
        INamedTypeSymbol n => n.TypeArguments.Any(t => MayBeRefLike(t, owner)),
        _ => false
    };

    private static bool NeedsScoped(Route route) => route.Request.IsRefLikeType;

    private static bool NeedsScoped(MultiRoute route) => route.Request.IsRefLikeType;

    // Direct call is safe: virtual dispatch on the concrete receiver reaches the same override.
    private static bool UseDirectCall(INamedTypeSymbol closed, INamedTypeSymbol ifaceDefinition, string methodName = "HandleAsync")
    {
        var interfaces = closed.AllInterfaces.Where(i => Same(i.OriginalDefinition, ifaceDefinition)).ToArray();
        // With multiple contracts, use the route-specific interface cast. A
        // public overload need not implement the contract for this request.
        if (interfaces.Length != 1) return false;
        var iface = interfaces[0];
        var target = iface?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        if (target == null) return false;
        if (closed.FindImplementationForInterfaceMember(target) is not IMethodSymbol impl) return false;
        return impl.ExplicitInterfaceImplementations.IsEmpty &&
            impl.ContainingType != null && Same(impl.ContainingType.OriginalDefinition, closed.OriginalDefinition);
    }

    private static string Emit(INamedTypeSymbol mediator, List<Route> routes, List<NotificationRoute> notifications, List<MultiRoute> multiRoutes, List<Route> syncRoutes, List<MultiRoute> syncMultiRoutes, List<Route> streamRoutes, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition, INamedTypeSymbol notificationHandlerDefinition, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition)
    {
        var ns = mediator.ContainingNamespace.IsGlobalNamespace ? null : mediator.ContainingNamespace.ToDisplayString();
        var prefix = ns == null ? "global::" : "global::" + ns + ".";
        return EmitCore(ns, prefix, Name(mediator), routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, null);
    }

    private static string EmitAssembly(string targetNamespace, List<Route> routes, List<NotificationRoute> notifications, List<MultiRoute> multiRoutes, List<Route> syncRoutes, List<MultiRoute> syncMultiRoutes, List<Route> streamRoutes, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition, INamedTypeSymbol notificationHandlerDefinition, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition, DiEmitOptions? di) =>
        EmitCore(targetNamespace, "global::" + targetNamespace + ".", "global::" + targetNamespace + ".Zendiator", routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, di);

    private static string EmitCore(string? targetNamespace, string prefix, string mediatorName, List<Route> routes, List<NotificationRoute> notifications, List<MultiRoute> multiRoutes, List<Route> syncRoutes, List<MultiRoute> syncMultiRoutes, List<Route> streamRoutes, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition, INamedTypeSymbol notificationHandlerDefinition, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition, DiEmitOptions? di)
    {
        var b = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        b.AppendLine("using global::Microsoft.Extensions.DependencyInjection;");
        if (targetNamespace != null)
            b.Append("namespace ").Append(targetNamespace).AppendLine(";");
        b.AppendLine("/// <summary>Typed request dispatch for this composition.</summary>\npublic interface IZendiator\n{");
        foreach (var route in routes)
        {
            b.AppendLine("    /// <summary>Dispatches the request through its configured pipeline.</summary>");
            b.Append("    ").Append(Signature(route)).AppendLine(";");
        }
        foreach (var notification in notifications)
        {
            b.AppendLine("    /// <summary>Publishes the notification to subscribers in order.</summary>");
            b.Append("    ").Append(NotificationSignature(notification, publish: false)).AppendLine(";");
            b.Append("    ").Append(NotificationSignature(notification, publish: true)).AppendLine(";");
        }
        if (notifications.Any(static n => !n.IsOpen))
        {
            b.AppendLine("    /// <summary>Publishes a registered notification by its runtime type.</summary>");
            b.Append("    ").Append(ErasedSignature(publish: false)).AppendLine(";");
            b.Append("    ").Append(ErasedSignature(publish: true)).AppendLine(";");
        }
        foreach (var multi in multiRoutes)
        {
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order.</summary>");
            b.Append("    ").Append(MultiSignature(multi)).AppendLine(";");
        }
        foreach (var sync in syncRoutes)
        {
            b.AppendLine("    /// <summary>Dispatches the request synchronously without retaining it.</summary>");
            b.Append("    ").Append(SyncSignature(sync)).AppendLine(";");
        }
        foreach (var sync in syncMultiRoutes)
        {
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>");
            b.Append("    ").Append(SyncMultiSignature(sync)).AppendLine(";");
        }
        foreach (var stream in streamRoutes)
        {
            b.AppendLine("    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>");
            b.Append("    ").Append(StreamSignature(stream)).AppendLine(";");
        }
        b.AppendLine("}\n/// <summary>Generated scoped mediator.</summary>\npublic sealed partial class Zendiator : IZendiator\n{");
        b.AppendLine("    private readonly global::System.IServiceProvider _services;\n    /// <summary>Creates a mediator bound to the supplied scope.</summary>\n    public Zendiator(global::System.IServiceProvider services)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(services);\n        _services = services;\n    }");
        // The container owns lifetime and reuse. A mutable IServiceCollection
        // cannot prove which descriptors a previously built provider captured.
        for (var index = 0; index < routes.Count; index++)
        {
            var route = routes[index];
            if (route.IsOpen)
            {
                EmitOpenRoute(b, route, index, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition);
                continue;
            }
            b.AppendLine("    /// <summary>Dispatches the request through its configured pipeline.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(Signature(route)).AppendLine("\n    {");
            if (route.Behaviors.Count == 0)
            {
                Guard(b, route, "        ");
                var direct = UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                var receiver = $"_services.GetRequiredService<{Name(route.Handler)}>()";
                if (!direct) receiver = "((" + HandlerContract(route) + ")" + receiver + ")";
                b.Append("        return ").Append(receiver).AppendLine(".HandleAsync(request, cancellationToken);\n    }");
                continue;
            }
            b.Append("        return new Route").Append(index).AppendLine("Node0(_services).InvokeAsync(request, cancellationToken);\n    }");
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.Append("    private readonly struct Route").Append(index).Append("Node").Append(node)
                    .Append("(global::System.IServiceProvider services) : ").Append(ContinuationContract(route)).AppendLine("\n    {");
                b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                b.Append("        public ").Append(TaskContract(route)).Append(" InvokeAsync(").Append(Name(route.Request))
                    .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                Guard(b, route, "            ");
                if (node == route.Behaviors.Count)
                {
                    if (UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition))
                        HandlerCallDirect(b, route);
                    else
                        HandlerCall(b, route, "services", "            ");
                }
                else
                {
                    var receiver = $"services.GetRequiredService<{Name(route.Behaviors[node])}>()";
                    if (!UseDirectCall(route.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition))
                        receiver = "((" + BehaviorContract(route) + ")" + receiver + ")";
                    b.Append("            return ").Append(receiver).Append(".HandleAsync(request, new Route")
                        .Append(index).Append("Node").Append(node + 1).AppendLine("(services), cancellationToken);");
                }
                b.AppendLine("        }\n    }");
            }
        }
        EmitNotifications(b, notifications, notificationHandlerDefinition);
        EmitMulti(b, multiRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition);
        EmitSyncRoutes(b, syncRoutes, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition);
        EmitSyncMulti(b, syncMultiRoutes, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition);
        EmitStreams(b, streamRoutes, streamHandlerDefinition, streamBehaviorDefinition);
        // Shared token-merge helper: link only when both tokens are cancellable and different.
        if (streamRoutes.Count != 0)
        {
            b.AppendLine("    private static global::System.Threading.CancellationToken MergeStreamTokens(global::System.Threading.CancellationToken apiToken, global::System.Threading.CancellationToken enumeratorToken, out global::System.Threading.CancellationTokenSource? linked)");
            b.AppendLine("    {");
            b.AppendLine("        linked = null;");
            b.AppendLine("        if (!apiToken.CanBeCanceled) return enumeratorToken;");
            b.AppendLine("        if (!enumeratorToken.CanBeCanceled) return apiToken;");
            b.AppendLine("        if (apiToken.Equals(enumeratorToken)) return apiToken;");
            b.AppendLine("        linked = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(apiToken, enumeratorToken);");
            b.AppendLine("        return linked.Token;");
            b.AppendLine("    }");
        }
        b.AppendLine("}");
        if (di == null || di.EmitExtensions)
        {
            b.AppendLine("/// <summary>Registers the generated mediator and its concrete services.</summary>\npublic static class ZendiatorServiceCollectionExtensions\n{");
            b.AppendLine("    /// <summary>Adds scoped defaults without replacing existing registrations.</summary>\n    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(services);\n        return AddZendiator(services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped);\n    }");
            b.AppendLine("    /// <summary>Adds defaults with the selected lifetime without replacing existing registrations. All registrations share the lifetime.</summary>\n    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(services);\n        if (lifetime is not (global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient))\n            throw new global::System.ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);");
            EmitRegistrationEntries(b, prefix, mediatorName, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, "lifetime");
            b.AppendLine("        return services;\n    }\n}");
        }
        else
        {
            EmitRegistrar(b, prefix, mediatorName, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, di.Fingerprint);
        }
        return b.ToString();
    }

    private static string EmitInterceptorFile(List<(int Version, string Data, bool IsLambda, bool IsExtensionForm)> shims, string registrarPrefix)
    {
        if (shims.Count == 0) return "";
        var b = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        EmitShims(b, shims, registrarPrefix);
        return b.ToString();
    }

    private static void EmitOpenRoute(StringBuilder b, Route route, int index, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition)
    {
        // Open routes resolve closed services per send; the shared DI container
        // keeps scoped/singleton reuse separated by closed type.
        var tp = string.Join(", ", route.OpenTypeParams);
        b.AppendLine("    /// <summary>Dispatches the request through its configured pipeline.</summary>");
        b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        b.Append("    public ").Append(Signature(route)).AppendLine("\n    {");
        Guard(b, route, "        ");
        b.Append("        return new OpenRoute").Append(index).Append("Node0<").Append(tp).Append(">(_services).InvokeAsync(request, cancellationToken);\n    }");
        for (var node = 0; node <= route.Behaviors.Count; node++)
        {
            b.Append("    private readonly struct OpenRoute").Append(index).Append("Node").Append(node).Append("<").Append(tp)
                .Append(">(global::System.IServiceProvider services) : ").Append(ContinuationContract(route));
            foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("        public ").Append(TaskContract(route)).Append(" InvokeAsync(").Append(route.RequestDisplay)
                .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
            Guard(b, route, "            ");
            if (node == route.Behaviors.Count)
            {
                var directH = UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                var castH = "((" + route.HandlerContractDisplay + ")";
                b.Append("            return ").Append(directH
                    ? $"services.GetRequiredService<{route.HandlerDisplay}>()"
                    : castH + $"services.GetRequiredService<{route.HandlerDisplay}>())").AppendLine(".HandleAsync(request, cancellationToken);");
            }
            else
            {
                var directB = UseDirectCall(route.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition);
                var castB = "((" + route.BehaviorContractDisplays[node] + ")";
                b.Append("            return ").Append(directB
                    ? $"services.GetRequiredService<{route.BehaviorDisplays[node]}>()"
                    : castB + $"services.GetRequiredService<{route.BehaviorDisplays[node]}>())")
                    .Append(".HandleAsync(request, new OpenRoute").Append(index).Append("Node").Append(node + 1)
                    .Append("<").Append(tp).Append(">(services), cancellationToken);");
            }
            b.AppendLine("        }\n    }");
        }
    }

    private static string NotificationSignature(NotificationRoute route, bool publish)
    {
        var verb = publish ? "Publish" : "PublishAsync";
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            return $"global::System.Threading.Tasks.ValueTask {verb}<{tp}>({route.NotificationDisplay} notification, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        return $"global::System.Threading.Tasks.ValueTask {verb}({route.NotificationDisplay} notification, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static string ErasedSignature(bool publish)
    {
        var verb = publish ? "Publish" : "PublishAsync";
        return $"global::System.Threading.Tasks.ValueTask {verb}(global::Zendiator.INotification notification, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static void EmitNotifications(StringBuilder b, List<NotificationRoute> notifications, INamedTypeSymbol notificationHandlerDefinition)
    {
        foreach (var route in notifications)
        {
            b.AppendLine("    /// <summary>Publishes the notification to subscribers in order.</summary>");
            if (route.Subscribers.Count == 0)
            {
                b.Append("    public ").Append(NotificationSignature(route, publish: false)).AppendLine("\n    {");
                if (route.Notification.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
                b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                b.AppendLine("        return default;\n    }");
            }
            else
            {
                b.Append("    public async ").Append(NotificationSignature(route, publish: false)).AppendLine("\n    {");
                if (route.Notification.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
                b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                for (var i = 0; i < route.Subscribers.Count; i++)
                {
                    var sub = route.Subscribers[i];
                    string handlerName, contract;
                    if (route.IsOpen)
                    {
                        handlerName = route.HandlerDisplays[i];
                        contract = route.HandlerContractDisplays[i];
                    }
                    else
                    {
                        handlerName = Name(sub.Handler);
                        contract = $"global::Zendiator.INotificationHandler<{route.NotificationDisplay}>";
                    }
                    var direct = UseDirectCall(sub.Handler, notificationHandlerDefinition);
                    var recv = direct
                        ? $"_services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")_services.GetRequiredService<{handlerName}>())";
                    b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                    b.Append("        await ").Append(recv).AppendLine(".HandleAsync(notification, cancellationToken).ConfigureAwait(false);");
                }
                b.AppendLine("    }");
            }
            b.Append("    public ").Append(NotificationSignature(route, publish: true)).Append(" => PublishAsync(notification, cancellationToken);\n");
        }
        var closed = notifications.Where(static n => !n.IsOpen).ToList();
        if (closed.Count != 0)
        {
            b.AppendLine("    /// <summary>Publishes a registered notification by its runtime type.</summary>");
            b.Append("    public ").Append(ErasedSignature(publish: false)).AppendLine("\n    {");
            b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            b.AppendLine("        var notificationType = notification.GetType();");
            foreach (var route in closed)
                b.Append("        if (notificationType == typeof(").Append(route.NotificationDisplay).Append(")) return PublishAsync((").Append(route.NotificationDisplay).AppendLine(")notification, cancellationToken);");
            b.AppendLine("        throw new global::System.InvalidOperationException($\"Unknown notification type '{notificationType}'. Include its assembly or declare it with [assembly: Notification].\");\n    }");
            b.Append("    public ").Append(ErasedSignature(publish: true)).Append(" => PublishAsync(notification, cancellationToken);\n");
        }
    }

    private static string MultiSignature(MultiRoute route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid
                ? "global::System.Threading.Tasks.ValueTask"
                : $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{route.ResponseDisplay}>>";
            return $"{task} SendAllAsync<{tp}>({route.RequestDisplay} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        var closedTask = route.IsVoid
            ? "global::System.Threading.Tasks.ValueTask"
            : $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{Name(route.Response)}>>";
        return $"{closedTask} SendAllAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static void EmitMulti(StringBuilder b, List<MultiRoute> multiRoutes, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition)
    {
        for (var index = 0; index < multiRoutes.Count; index++)
        {
            var route = multiRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var reqDisplay = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var respDisplay = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var contDisplay = route.IsVoid
                ? $"global::Zendiator.IRequestContinuation<{reqDisplay}>"
                : $"global::Zendiator.IRequestContinuation<{reqDisplay}, {respDisplay}>";
            var taskDisplay = route.IsVoid
                ? "global::System.Threading.Tasks.ValueTask"
                : $"global::System.Threading.Tasks.ValueTask<{respDisplay}>";
            var branchContract = route.IsVoid
                ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
            var behaviorRouteContract = route.IsVoid
                ? $"global::Zendiator.IPipelineBehavior<{reqDisplay}>"
                : $"global::Zendiator.IPipelineBehavior<{reqDisplay}, {respDisplay}>";
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public async ").Append(MultiSignature(route)).AppendLine("\n    {");
            if (route.Request.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(request);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            if (!route.IsVoid)
                b.Append("        var results = new global::System.Collections.Generic.List<").Append(respDisplay).Append(">(").Append(route.Branches.Count).AppendLine(");");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var node0 = $"MultiRoute{index}Branch{bh}Node0{tp}";
                if (route.IsVoid)
                    b.Append("        await new ").Append(node0).AppendLine("(_services).InvokeAsync(request, cancellationToken).ConfigureAwait(false);");
                else
                    b.Append("        results.Add(await new ").Append(node0).AppendLine("(_services).InvokeAsync(request, cancellationToken).ConfigureAwait(false));");
            }
            if (!route.IsVoid) b.AppendLine("        return results;");
            b.AppendLine("    }");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var branch = route.Branches[bh];
                var prefix = $"MultiRoute{index}Branch{bh}";
                for (var node = 0; node <= branch.Behaviors.Count; node++)
                {
                    b.Append("    private readonly struct ").Append(prefix).Append("Node").Append(node);
                    if (route.IsOpen) b.Append(tp);
                    b.Append("(global::System.IServiceProvider services) : ").Append(contDisplay);
                    if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                    b.AppendLine("\n    {");
                    b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                    b.Append("        public ").Append(taskDisplay).Append(" InvokeAsync(").Append(reqDisplay)
                        .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                    if (route.Request.IsReferenceType) b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(request);");
                    b.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
                    if (node == branch.Behaviors.Count)
                    {
                        var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                        var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                        var direct = UseDirectCall(branch.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                        var recv = direct
                            ? $"services.GetRequiredService<{handlerName}>()"
                            : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                        b.Append("            return ").Append(recv).AppendLine(".HandleAsync(request, cancellationToken);");
                    }
                    else
                    {
                        var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                        var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                        var direct = UseDirectCall(branch.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition);
                        var recv = direct
                            ? $"services.GetRequiredService<{behaviorName}>()"
                            : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                        b.Append("            return ").Append(recv)
                            .Append(".HandleAsync(request, new ").Append(prefix).Append("Node").Append(node + 1);
                        if (route.IsOpen) b.Append(tp);
                        b.AppendLine("(services), cancellationToken);");
                    }
                    b.AppendLine("        }\n    }");
                }
            }
        }
    }

    private static string SyncSignature(Route route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid ? "void" : (route.IsOpen ? route.ResponseDisplay : Name(route.Response));
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $"{task} SendSync{tp}({scoped}{req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static string SyncMultiSignature(MultiRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid
            ? "void"
            : $"global::System.Collections.Generic.IReadOnlyList<{(route.IsOpen ? route.ResponseDisplay : Name(route.Response))}>";
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $"{task} SendAllSync{tp}({scoped}{req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static void EmitSyncRoutes(StringBuilder b, List<Route> syncRoutes, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncRoutes.Count; index++)
        {
            var route = syncRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var task = route.IsVoid ? "void" : resp;
            var cont = route.IsVoid
                ? $"global::Zendiator.ISyncRequestContinuation<{req}>"
                : $"global::Zendiator.ISyncRequestContinuation<{req}, {resp}>";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine("    /// <summary>Dispatches the request synchronously without retaining it.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(SyncSignature(route)).AppendLine("\n    {");
            Guard(b, route, "        ");
            b.Append("        ");
            if (!route.IsVoid) b.Append("return ");
            b.Append("new SyncRoute").Append(index).Append("Node0").Append(tp).Append("(_services).Invoke(request, cancellationToken);\n    }");
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.Append("    private readonly struct SyncRoute").Append(index).Append("Node").Append(node);
                if (route.IsOpen) b.Append(tp);
                b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                b.AppendLine("\n    {");
                b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                b.Append("        public ").Append(task).Append(" Invoke(").Append(scoped).Append(req)
                    .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                Guard(b, route, "            ");
                if (node == route.Behaviors.Count)
                {
                    string handlerName, contract;
                    if (route.IsOpen)
                    {
                        handlerName = route.HandlerDisplay;
                        contract = route.HandlerContractDisplay;
                    }
                    else
                    {
                        handlerName = Name(route.Handler);
                        contract = route.IsVoid
                            ? $"global::Zendiator.ISyncRequestHandler<{req}>"
                            : $"global::Zendiator.ISyncRequestHandler<{req}, {resp}>";
                    }
                    var direct = UseDirectCall(route.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                    var recv = direct
                        ? $"services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                    b.Append("            ");
                    if (!route.IsVoid) b.Append("return ");
                    b.Append(recv).AppendLine(".Handle(request, cancellationToken);");
                }
                else
                {
                    string behaviorName, contract;
                    if (route.IsOpen)
                    {
                        behaviorName = route.BehaviorDisplays[node];
                        contract = route.BehaviorContractDisplays[node];
                    }
                    else
                    {
                        behaviorName = Name(route.Behaviors[node]);
                        contract = route.IsVoid
                            ? $"global::Zendiator.ISyncPipelineBehavior<{req}>"
                            : $"global::Zendiator.ISyncPipelineBehavior<{req}, {resp}>";
                    }
                    var direct = UseDirectCall(route.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                    var recv = direct
                        ? $"services.GetRequiredService<{behaviorName}>()"
                        : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                    b.Append("            ");
                    if (!route.IsVoid) b.Append("return ");
                    b.Append(recv).Append(".Handle(request, new SyncRoute").Append(index).Append("Node").Append(node + 1);
                    if (route.IsOpen) b.Append(tp);
                    b.AppendLine("(services), cancellationToken);");
                }
                b.AppendLine("        }\n    }");
            }
        }
    }

    private static void EmitSyncMulti(StringBuilder b, List<MultiRoute> syncMultiRoutes, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncMultiRoutes.Count; index++)
        {
            var route = syncMultiRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = route.IsVoid
                ? $"global::Zendiator.ISyncRequestContinuation<{req}>"
                : $"global::Zendiator.ISyncRequestContinuation<{req}, {resp}>";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(SyncMultiSignature(route)).AppendLine("\n    {");
            if (route.Request.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(request);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            if (!route.IsVoid)
                b.Append("        var results = new global::System.Collections.Generic.List<").Append(resp).Append(">(").Append(route.Branches.Count).AppendLine(");");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var node0 = $"SyncMultiRoute{index}Branch{bh}Node0{tp}";
                if (route.IsVoid)
                    b.Append("        new ").Append(node0).AppendLine("(_services).Invoke(request, cancellationToken);");
                else
                    b.Append("        results.Add(new ").Append(node0).AppendLine("(_services).Invoke(request, cancellationToken));");
            }
            if (!route.IsVoid) b.AppendLine("        return results;");
            b.AppendLine("    }");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var branch = route.Branches[bh];
                var prefix = $"SyncMultiRoute{index}Branch{bh}";
                var branchContract = route.IsVoid
                    ? $"global::Zendiator.ISyncRequestHandler<{req}>"
                    : $"global::Zendiator.ISyncRequestHandler<{req}, {resp}>";
                var behaviorRouteContract = route.IsVoid
                    ? $"global::Zendiator.ISyncPipelineBehavior<{req}>"
                    : $"global::Zendiator.ISyncPipelineBehavior<{req}, {resp}>";
                for (var node = 0; node <= branch.Behaviors.Count; node++)
                {
                    b.Append("    private readonly struct ").Append(prefix).Append("Node").Append(node);
                    if (route.IsOpen) b.Append(tp);
                    b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                    if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                    b.AppendLine("\n    {");
                    b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                    b.Append("        public ").Append(route.IsVoid ? "void" : resp).Append(" Invoke(").Append(scoped).Append(req)
                        .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                    if (route.Request.IsReferenceType) b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(request);");
                    b.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
                    if (node == branch.Behaviors.Count)
                    {
                        var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                        var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                        var direct = UseDirectCall(branch.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                        var recv = direct
                            ? $"services.GetRequiredService<{handlerName}>()"
                            : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                        b.Append("            ");
                        if (!route.IsVoid) b.Append("return ");
                        b.Append(recv).AppendLine(".Handle(request, cancellationToken);");
                    }
                    else
                    {
                        var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                        var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                        var direct = UseDirectCall(branch.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                        var recv = direct
                            ? $"services.GetRequiredService<{behaviorName}>()"
                            : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                        b.Append("            ");
                        if (!route.IsVoid) b.Append("return ");
                        b.Append(recv).Append(".Handle(request, new ").Append(prefix).Append("Node").Append(node + 1);
                        if (route.IsOpen) b.Append(tp);
                        b.AppendLine("(services), cancellationToken);");
                    }
                    b.AppendLine("        }\n    }");
                }
            }
        }
    }

    private static string StreamSignature(Route route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        return $"global::System.Collections.Generic.IAsyncEnumerable<{item}> StreamAsync{tp}({req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static void EmitStreams(StringBuilder b, List<Route> streamRoutes, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition)
    {
        for (var index = 0; index < streamRoutes.Count; index++)
        {
            var route = streamRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = $"global::Zendiator.IStreamContinuation<{req}, {item}>";
            var enumerable = $"StreamRoute{index}Enumerable";
            var enumerator = $"StreamRoute{index}Enumerator";
            b.AppendLine("    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(StreamSignature(route)).AppendLine("\n    {");
            // Lazy: no handler execution, no DI resolution here. Only capture scope provider + request + token.
            b.Append("        return new ").Append(enumerable).Append(tp).AppendLine("(_services, request, cancellationToken);\n    }");
            // Typed enumerable: lightweight, no shared mutable state on the mediator.
            b.Append("    private sealed class ").Append(enumerable);
            if (route.IsOpen) b.Append(tp);
            b.Append("(global::System.IServiceProvider services, ").Append(req).Append(" request, global::System.Threading.CancellationToken apiToken) : global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append(">");
            if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.Append("        public global::System.Collections.Generic.IAsyncEnumerator<").Append(item).Append(">").Append(" GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken = default) => new ").Append(enumerator).Append(tp).AppendLine("(services, request, apiToken, cancellationToken);");
            b.AppendLine("    }");
            // Typed enumerator: resolves pipeline once on first MoveNext, merges tokens only when different.
            b.Append("    private sealed class ").Append(enumerator);
            if (route.IsOpen) b.Append(tp);
            b.Append(" : global::System.Collections.Generic.IAsyncEnumerator<").Append(item).Append(">");
            if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.AppendLine("        private readonly global::System.IServiceProvider _services;");
            b.Append("        private readonly ").Append(req).AppendLine(" _request;");
            b.AppendLine("        private readonly global::System.Threading.CancellationToken _apiToken;");
            b.AppendLine("        private readonly global::System.Threading.CancellationToken _enumToken;");
            b.AppendLine("        private global::System.Threading.CancellationTokenSource? _linked;");
            b.Append("        private global::System.Collections.Generic.IAsyncEnumerator<").Append(item).AppendLine(">? _inner;");
            b.AppendLine("        private bool _started;");
            b.AppendLine("        private bool _done;");
            b.Append("        public ").Append(enumerator).Append("(global::System.IServiceProvider services, ").Append(req).AppendLine(" request, global::System.Threading.CancellationToken apiToken, global::System.Threading.CancellationToken enumToken)");
            b.AppendLine("        {");
            b.AppendLine("            _services = services;");
            b.AppendLine("            _request = request;");
            b.AppendLine("            _apiToken = apiToken;");
            b.AppendLine("            _enumToken = enumToken;");
            b.AppendLine("        }");
            b.Append("        public ").Append(item).AppendLine(" Current => _inner != null ? _inner.Current : default!;");
            b.AppendLine("        public async global::System.Threading.Tasks.ValueTask<bool> MoveNextAsync()");
            b.AppendLine("        {");
            b.AppendLine("            if (_done) return false;");
            b.AppendLine("            if (!_started)");
            b.AppendLine("            {");
            b.AppendLine("                _started = true;");
            if (route.Request.IsReferenceType)
                b.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(_request);");
            b.AppendLine("                var effective = MergeStreamTokens(_apiToken, _enumToken, out var linked);");
            b.AppendLine("                _linked = linked;");
            b.AppendLine("                effective.ThrowIfCancellationRequested();");
            b.Append("                global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append("> pipeline = new StreamRoute").Append(index).Append("Node0").Append(tp).Append("(_services).InvokeAsync(_request, effective);");
            b.AppendLine("");
            b.Append("                _inner = pipeline.GetAsyncEnumerator(effective);");
            b.AppendLine("");
            b.AppendLine("            }");
            b.AppendLine("            try");
            b.AppendLine("            {");
            b.AppendLine("                var ok = await _inner!.MoveNextAsync().ConfigureAwait(false);");
            b.AppendLine("                if (!ok) _done = true;");
            b.AppendLine("                return ok;");
            b.AppendLine("            }");
            b.AppendLine("            catch");
            b.AppendLine("            {");
            b.AppendLine("                _done = true;");
            b.AppendLine("                throw;");
            b.AppendLine("            }");
            b.AppendLine("        }");
            b.AppendLine("        public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
            b.AppendLine("        {");
            b.AppendLine("            _done = true;");
            b.AppendLine("            var inner = _inner;");
            b.AppendLine("            _inner = null;");
            b.AppendLine("            try");
            b.AppendLine("            {");
            b.AppendLine("                if (inner != null) await inner.DisposeAsync().ConfigureAwait(false);");
            b.AppendLine("            }");
            b.AppendLine("            finally");
            b.AppendLine("            {");
            b.AppendLine("                _linked?.Dispose();");
            b.AppendLine("                _linked = null;");
            b.AppendLine("            }");
            b.AppendLine("        }");
            b.AppendLine("    }");
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.Append("    private readonly struct StreamRoute").Append(index).Append("Node").Append(node);
                if (route.IsOpen) b.Append(tp);
                b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                b.AppendLine("\n    {");
                b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                b.Append("        public global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append("> InvokeAsync(").Append(req)
                    .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                if (node == route.Behaviors.Count)
                {
                    string handlerName, contract;
                    if (route.IsOpen)
                    {
                        handlerName = route.HandlerDisplay;
                        contract = route.HandlerContractDisplay;
                    }
                    else
                    {
                        handlerName = Name(route.Handler);
                        contract = $"global::Zendiator.IStreamRequestHandler<{req}, {item}>";
                    }
                    var direct = UseDirectCall(route.Handler, streamHandlerDefinition);
                    var recv = direct
                        ? $"services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                    b.Append("            return ").Append(recv).AppendLine(".HandleAsync(request, cancellationToken);");
                }
                else
                {
                    string behaviorName, contract;
                    if (route.IsOpen)
                    {
                        behaviorName = route.BehaviorDisplays[node];
                        contract = route.BehaviorContractDisplays[node];
                    }
                    else
                    {
                        behaviorName = Name(route.Behaviors[node]);
                        contract = $"global::Zendiator.IStreamPipelineBehavior<{req}, {item}>";
                    }
                    var direct = UseDirectCall(route.Behaviors[node], streamBehaviorDefinition);
                    var recv = direct
                        ? $"services.GetRequiredService<{behaviorName}>()"
                        : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                    b.Append("            return ").Append(recv)
                        .Append(".HandleAsync(request, new StreamRoute").Append(index).Append("Node").Append(node + 1);
                    if (route.IsOpen) b.Append(tp);
                    b.AppendLine("(services), cancellationToken);");
                }
                b.AppendLine("        }\n    }");
            }
        }
    }

    private static void Register(StringBuilder b, string arguments, string lifetime)
    {
        b.Append("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe(")
            .Append(arguments).Append(", ").Append(lifetime).AppendLine("));");
    }

    private static void EmitRegistrationEntries(StringBuilder b, string prefix, string mediatorName, List<Route> routes, List<NotificationRoute> notifications, List<MultiRoute> multiRoutes, List<Route> syncRoutes, List<MultiRoute> syncMultiRoutes, List<Route> streamRoutes, string lifetime)
    {
        var mediatorNameLocal = mediatorName;
        Register(b, $"typeof({mediatorNameLocal}), typeof({mediatorNameLocal})", lifetime);
        b.Append("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe(typeof(")
            .Append(prefix).Append("IZendiator), static provider => (object)provider.GetRequiredService<").Append(mediatorName).Append(">(), ").Append(lifetime).AppendLine("));");
        foreach (var service in routes.SelectMany(r => r.Behaviors.Concat(new[] { r.Handler }).Select(s => (Route: r, Service: s)))
            .Select(p => p.Route.IsOpen ? $"typeof({OpenTypeofName(p.Service)}), typeof({OpenTypeofName(p.Service)})" : $"typeof({Name(p.Service)}), typeof({Name(p.Service)})")
            .Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var notificationServices = new List<string>();
        foreach (var notification in notifications)
        {
            if (notification.IsOpen)
            {
                foreach (var sub in notification.Subscribers)
                    notificationServices.Add($"typeof({OpenTypeofName(sub.Handler)}), typeof({OpenTypeofName(sub.Handler)})");
            }
            else
            {
                foreach (var sub in notification.Subscribers)
                    notificationServices.Add($"typeof({Name(sub.Handler)}), typeof({Name(sub.Handler)})");
            }
        }
        foreach (var service in notificationServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var multiServices = new List<string>();
        foreach (var multi in multiRoutes)
        {
            foreach (var branch in multi.Branches)
            {
                multiServices.Add(branch.HandlerIsOpen
                    ? $"typeof({OpenTypeofName(branch.Handler)}), typeof({OpenTypeofName(branch.Handler)})"
                    : $"typeof({Name(branch.Handler)}), typeof({Name(branch.Handler)})");
                foreach (var behavior in branch.Behaviors)
                {
                    multiServices.Add(multi.IsOpen
                        ? $"typeof({OpenTypeofName(behavior)}), typeof({OpenTypeofName(behavior)})"
                        : $"typeof({Name(behavior)}), typeof({Name(behavior)})");
                }
            }
        }
        foreach (var service in multiServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var syncServices = new List<string>();
        foreach (var route in syncRoutes)
        {
            foreach (var behavior in route.Behaviors)
            {
                syncServices.Add(route.IsOpen
                    ? $"typeof({OpenTypeofName(behavior)}), typeof({OpenTypeofName(behavior)})"
                    : $"typeof({Name(behavior)}), typeof({Name(behavior)})");
            }
            syncServices.Add(route.IsOpen
                ? $"typeof({OpenTypeofName(route.Handler)}), typeof({OpenTypeofName(route.Handler)})"
                : $"typeof({Name(route.Handler)}), typeof({Name(route.Handler)})");
        }
        foreach (var multi in syncMultiRoutes)
        {
            foreach (var branch in multi.Branches)
            {
                syncServices.Add(branch.HandlerIsOpen
                    ? $"typeof({OpenTypeofName(branch.Handler)}), typeof({OpenTypeofName(branch.Handler)})"
                    : $"typeof({Name(branch.Handler)}), typeof({Name(branch.Handler)})");
                foreach (var behavior in branch.Behaviors)
                {
                    syncServices.Add(multi.IsOpen
                        ? $"typeof({OpenTypeofName(behavior)}), typeof({OpenTypeofName(behavior)})"
                        : $"typeof({Name(behavior)}), typeof({Name(behavior)})");
                }
            }
        }
        foreach (var service in syncServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var streamServices = new List<string>();
        foreach (var route in streamRoutes)
        {
            foreach (var behavior in route.Behaviors)
            {
                streamServices.Add(route.IsOpen
                    ? $"typeof({OpenTypeofName(behavior)}), typeof({OpenTypeofName(behavior)})"
                    : $"typeof({Name(behavior)}), typeof({Name(behavior)})");
            }
            streamServices.Add(route.IsOpen
                ? $"typeof({OpenTypeofName(route.Handler)}), typeof({OpenTypeofName(route.Handler)})"
                : $"typeof({Name(route.Handler)}), typeof({Name(route.Handler)})");
        }
        foreach (var service in streamServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
    }

    private static void EmitRegistrar(StringBuilder b, string prefix, string mediatorName, List<Route> routes, List<NotificationRoute> notifications, List<MultiRoute> multiRoutes, List<Route> syncRoutes, List<MultiRoute> syncMultiRoutes, List<Route> streamRoutes, string fingerprint)
    {
        b.AppendLine("/// <summary>Generated DI registrar. Prefer AddZendiator; do not call directly.</summary>");
        b.AppendLine("internal static class ZendiatorGeneratedRegistrar");
        b.AppendLine("{");
        b.Append("    internal const string StructureFingerprint = \"").Append(EscapeString(fingerprint)).AppendLine("\";");
        b.AppendLine("    internal static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot)");
        b.AppendLine("    {");
        b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);");
        b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(snapshot);");
        b.AppendLine("        if (snapshot.GetFingerprint() != StructureFingerprint)");
        b.AppendLine("            throw new global::System.InvalidOperationException(\"AddZendiator configuration does not match the generated structure. Keep one configuration per compilation.\");");
        EmitRegistrationEntries(b, prefix, mediatorName, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, "snapshot.ServiceLifetime");
        b.AppendLine("        return services;");
        b.AppendLine("    }");
        b.AppendLine("}");
    }

    private static void EmitShims(StringBuilder b, List<(int Version, string Data, bool IsLambda, bool IsExtensionForm)> shims, string registrarPrefix)
    {
        b.AppendLine("namespace System.Runtime.CompilerServices");
        b.AppendLine("{");
        b.AppendLine("    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]");
        b.AppendLine("    file sealed class InterceptsLocationAttribute : System.Attribute");
        b.AppendLine("    {");
        b.AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
        b.AppendLine("    }");
        b.AppendLine("}");
        b.AppendLine("namespace Zendiator.Generated.Interceptors");
        b.AppendLine("{");
        b.AppendLine("    static class AddZendiatorShims");
        b.AppendLine("    {");
        var index = 0;
        foreach (var (version, data, isLambda, isExtensionForm) in shims)
        {
            var name = "Shim" + index++;
            var receiver = isExtensionForm ? "this " : "";
            b.Append("        [System.Runtime.CompilerServices.InterceptsLocation(").Append(version).Append(", \"").Append(EscapeString(data)).AppendLine("\")]");
            if (isLambda)
            {
                b.Append("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection ").Append(name).Append("(").Append(receiver).AppendLine("global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::System.Action<global::Zendiator.DependencyInjection.ZendiatorConfiguration>? configure)");
                b.AppendLine("        {");
                b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(configure);");
                b.AppendLine("            var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();");
                b.AppendLine("            configure(configuration);");
                b.Append("            return ").Append(registrarPrefix).AppendLine("ZendiatorGeneratedRegistrar.Add(services, configuration.Snapshot());");
                b.AppendLine("        }");
            }
            else
            {
                b.Append("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection ").Append(name).Append("(").Append(receiver).AppendLine("global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
                b.AppendLine("        {");
                b.AppendLine("            var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();");
                b.Append("            return ").Append(registrarPrefix).AppendLine("ZendiatorGeneratedRegistrar.Add(services, configuration.Snapshot());");
                b.AppendLine("        }");
            }
        }
        b.AppendLine("    }");
        b.AppendLine("}");
    }

    private static string Signature(Route route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $"global::System.Threading.Tasks.ValueTask<{route.ResponseDisplay}>";
            return $"{task} SendAsync<{tp}>({route.RequestDisplay} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        return route.IsVoid
        ? $"global::System.Threading.Tasks.ValueTask SendAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)"
        : $"global::System.Threading.Tasks.ValueTask<{Name(route.Response)}> SendAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)";
    }
    private static string HandlerContract(Route route) => route.IsVoid
        ? $"global::Zendiator.IRequestHandler<{Name(route.Request)}>"
        : $"global::Zendiator.IRequestHandler<{Name(route.Request)}, {Name(route.Response)}>";
    private static string BehaviorContract(Route route) => route.IsVoid
        ? $"global::Zendiator.IPipelineBehavior<{Name(route.Request)}>"
        : $"global::Zendiator.IPipelineBehavior<{Name(route.Request)}, {Name(route.Response)}>";
    private static string ContinuationContract(Route route)
    {
        if (route.IsOpen) return route.IsVoid
            ? $"global::Zendiator.IRequestContinuation<{route.RequestDisplay}>"
            : $"global::Zendiator.IRequestContinuation<{route.RequestDisplay}, {route.ResponseDisplay}>";
        return route.IsVoid
        ? $"global::Zendiator.IRequestContinuation<{Name(route.Request)}>"
        : $"global::Zendiator.IRequestContinuation<{Name(route.Request)}, {Name(route.Response)}>";
    }
    private static string TaskContract(Route route)
    {
        if (route.IsOpen) return route.IsVoid
            ? "global::System.Threading.Tasks.ValueTask"
            : $"global::System.Threading.Tasks.ValueTask<{route.ResponseDisplay}>";
        return route.IsVoid
        ? "global::System.Threading.Tasks.ValueTask"
        : $"global::System.Threading.Tasks.ValueTask<{Name(route.Response)}>";
    }
    private static void HandlerCall(StringBuilder b, Route route, string services, string indent)
    {
        b.Append(indent).Append("return ((").Append(HandlerContract(route)).Append(")").Append(services).Append(".GetRequiredService<").Append(Name(route.Handler))
            .AppendLine(">()).HandleAsync(request, cancellationToken);");
    }
    private static void HandlerCallDirect(StringBuilder b, Route route)
    {
        b.Append("            return services.GetRequiredService<").Append(Name(route.Handler))
            .AppendLine(">().HandleAsync(request, cancellationToken);");
    }
    private static void Guard(StringBuilder b, Route route, string indent)
    {
        if (route.Request.IsReferenceType) b.Append(indent).AppendLine("global::System.ArgumentNullException.ThrowIfNull(request);");
        b.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
    }

}
