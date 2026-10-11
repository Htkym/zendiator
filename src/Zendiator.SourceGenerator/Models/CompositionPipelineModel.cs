namespace Zendiator.SourceGenerator;

internal enum BoundDispatchFamily { SendAsync, SendSync, SendAllAsync, SendAllSync, Stream, Notification }
internal enum BoundCallbackPhase { Before, Success, Exception, Finally, Filter, Map, Wrapper, Handler }
internal enum BoundCompletionKind { Synchronous, ValueTask, Enumerable }
internal sealed record BoundParameterModel(string Type, string RefKind, string ScopedKind);
internal sealed record BoundCallbackModel(BoundCallbackPhase Phase, BoundCompletionKind Completion,
    string Interface, string Implementation, EquatableArray<BoundParameterModel> Parameters,
    string ReturnType, string GenericConstraints, bool RequiresSingleConsumption);
internal sealed record BoundStageModel(string Anchor, string Type, string Name, FusionStageKind Kind,
    EquatableArray<string> Hierarchy, EquatableArray<BoundCallbackModel> Callbacks,
    FusionOwnership ServiceOwnership, bool CompilerOwnsError);
internal sealed record BoundHandlerModel(string Anchor, string Type, string Name, string Contract, string? Result,
    string Implementation, FusionOwnership ServiceOwnership, bool CompilerOwnsError, BoundCallbackModel? Callback);
internal sealed record BoundPipelineModel(string Anchor, string Request, string RequestName,
    BoundDispatchFamily? Family, FusionResultKind? ResultKind, string? Result,
    EquatableArray<BoundHandlerModel> Handlers, EquatableArray<BoundStageModel> Stages,
    bool ExplicitHandlerOrder, FusionContractOrigin ContractOrigin);
internal sealed record CompositionDiagnosticFact(string Key, string Anchor, string Message, string? AdditionalAnchor = null);
