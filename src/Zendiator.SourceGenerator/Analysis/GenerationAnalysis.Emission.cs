using static Zendiator.SourceGenerator.SourceEmitter;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private GenerationResult EmitResult()
    {
        if (errors.Count != 0) return new GenerationResult("", "", errors);
        if (diMode)
        {
            var options = new DiEmitOptions { EmitExtensions = false, Fingerprint = diFingerprint };
            var mediatorSource = EmitAssembly(diNamespace!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, options);
            return new GenerationResult(mediatorSource, EmitInterceptorFile(diCallSites, "global::" + diNamespace! + "."), errors);
        }
        return assemblyMode
            ? new GenerationResult(EmitAssembly(assemblyNamespace!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition, null), "", errors)
            : new GenerationResult(Emit(mediator!, routes, notifications, multiRoutes, syncRoutes, syncMultiRoutes, streamRoutes, handlerDefinition, behaviorDefinition, voidHandlerDefinition, voidBehaviorDefinition, notificationHandlerDefinition, syncHandlerDefinition, syncVoidHandlerDefinition, syncBehaviorDefinition, syncVoidBehaviorDefinition, streamHandlerDefinition, streamBehaviorDefinition), "", errors);
    }
}
