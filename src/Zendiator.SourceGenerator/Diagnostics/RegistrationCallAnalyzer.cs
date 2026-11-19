using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Zendiator.SourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegistrationCallAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GeneratorDiagnostics.InterceptionConnectionFailure);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var runtime = start.Compilation.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfiguration")?.ContainingAssembly;
            var registration = runtime?.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions");
            if (registration == null)
                return;
            start.RegisterOperationAction(c =>
            {
                var reference = (IMethodReferenceOperation)c.Operation;
                if (reference.Method.Name == "AddZendiator" &&
                    reference.Parent is IDelegateCreationOperation or IAddressOfOperation &&
                    SymbolEqualityComparer.Default.Equals(reference.Method.ContainingType, registration))
                    c.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.InterceptionConnectionFailure, reference.Syntax.GetLocation(),
                        "AddZendiator must be called directly so its registration can be generated. Wrap a direct call in a method or lambda when a delegate is needed."));
            }, OperationKind.MethodReference);
        });
    }
}
