namespace Zendiator.SourceGenerator;

// Typed callable names are captured from current symbols, independently of diagnostic identities.
internal sealed record BoundStreamHandlerEmission(string RequestName, string ItemName, string HandlerName,
    string ContractName, string MethodName, bool RequestIsValueType, bool HandlerIsValueType);
internal sealed record BoundStreamHookEmission(string HookName, string ContractName, string MethodName,
    BoundCallbackPhase Phase, bool HookIsValueType);

// C03 projection reuses the accepted schedule. Terminal cleanup invokes its existing Dispose subtree.
// This is an unregistered C04 prototype; it does not accept a public canonical runtime route.
internal sealed record CanonicalStreamPrototypeModel(string Namespace, string ClassName, string HintName,
    CanonicalStreamExecutionSchedule Schedule, StreamSiteId TerminalCleanupEntry,
    BoundStreamHandlerEmission Handler, EquatableArray<BoundStreamHookEmission> Hooks);
