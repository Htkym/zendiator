namespace Zendiator.SourceGenerator;

internal sealed record CanonicalStreamFallbackSegment(int Id, int? Upstream,
    BoundWrapperEmission? Wrapper, EquatableArray<BoundStreamHookEmission> Hooks,
    CanonicalStreamExecutionSchedule Schedule)
{
    public string? SourceItemName { get; init; }
    public string? OutputItemName { get; init; }
    public BoundAsyncFactoryEmission? WrapperFactory { get; init; }
}
internal sealed record CanonicalStreamFallbackModel(string Namespace, string ClassName, string HintName,
    BoundStreamHandlerEmission Handler, EquatableArray<CanonicalStreamFallbackSegment> Segments, int ConsumerSegment)
{
    public BoundStreamGenericEmission? Generic { get; init; }
    public BoundStreamItemFlow? ItemFlow { get; init; }
    public BoundAsyncFactoryEmission? HandlerFactory { get; init; }
    internal bool HandlerOwned => Handler.OwnsStream && HandlerFactory == null && ItemFlow == null && Segments.Count == 1 &&
        Segments[0].Wrapper == null && Segments[0].Hooks.Count == 0;
}
