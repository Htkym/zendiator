namespace Zendiator.SourceGenerator;

// Prototype ports are distinct values. No same-type BeforeResult/saved-request transfer is reused.
internal sealed record TypedSendPort(string Identity, string Name, string NullableAnnotation, bool IsReferenceType);
internal sealed record TypedSendCall(string ContractName, string MethodName);
internal sealed record TypedSendMap(string ReceiverName, TypedSendPort SavedRequest, TypedSendPort Input,
    TypedSendPort Output, TypedSendCall Map, TypedSendCall Exception, TypedSendCall Finally);
internal sealed record TypedSendPrototypeModel(string ClassName, TypedSendPort ExternalRequest, TypedSendPort PublicResponse,
    TypedSendPort HandlerRequest, TypedSendPort HandlerResponse, string HandlerName, TypedSendCall Handler,
    TypedSendMap RequestMap, TypedSendMap ResponseMap);
