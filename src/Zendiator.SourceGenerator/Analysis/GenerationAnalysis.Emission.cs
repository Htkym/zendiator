using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private GenerationResult EmitResult()
    {
        if (errors.Count != 0) return new GenerationResult(null, null, errors);
        var target = CreateGenerationTarget();
        var model = new GenerationModel(target with { CallSites = default }, CreateGenerationRoutes());

        return new GenerationResult(model, target, errors);
    }

    private GenerationTarget CreateGenerationTarget()
    {
        if (diMode)
            return new GenerationTarget(diNamespace!, "global::" + diNamespace! + ".Zendiator", diFingerprint, diCallSites);
        if (assemblyMode)
            return new GenerationTarget(assemblyNamespace!, "global::" + assemblyNamespace! + ".Zendiator");
        var targetNamespace = mediator!.ContainingNamespace.IsGlobalNamespace ? null : mediator.ContainingNamespace.ToDisplayString();
        return new GenerationTarget(targetNamespace, Name(mediator))
        {
            // An explicit object base in any partial declaration must remain the direct base.
            InheritServiceResolver = !mediator.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(ct)).OfType<ClassDeclarationSyntax>().Any(s => s.BaseList != null),
        };
    }

    private GenerationRoutes CreateGenerationRoutes()
    {
        var factory = new EmissionModelFactory(CreateGenerationContracts(), Name);
        return new GenerationRoutes(
            new RequestRoutes(new(routes.Select(factory.Request)), new(multiRoutes.Select(factory.Multiple))),
            new RequestRoutes(new(syncRoutes.Select(factory.Request)), new(syncMultiRoutes.Select(factory.Multiple))),
            new(notifications.Select(factory.Notification)),
            new(streamRoutes.Select(factory.Stream)));
    }

    private GenerationContracts CreateGenerationContracts() => new(
        new RequestContracts(handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition),
        new RequestContracts(syncHandlerDefinition, syncBehaviorDefinition, syncVoidHandlerDefinition, syncVoidBehaviorDefinition),
        new PipelineContracts(streamHandlerDefinition, streamBehaviorDefinition),
        notificationHandlerDefinition);
}
