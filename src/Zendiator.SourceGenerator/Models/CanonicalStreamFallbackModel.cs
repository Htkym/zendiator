namespace Zendiator.SourceGenerator;

// Typed projection of the existing validated Stream/Wrapper segments.
internal sealed record CanonicalStreamFallbackSegment(int Id, int? Upstream,
    BoundWrapperEmission? Wrapper, EquatableArray<BoundStreamHookEmission> Hooks,
    CanonicalStreamExecutionSchedule Schedule);
internal sealed record CanonicalStreamFallbackModel(string Namespace, string ClassName, string HintName,
    BoundStreamHandlerEmission Handler, EquatableArray<CanonicalStreamFallbackSegment> Segments, int ConsumerSegment)
{
    public BoundStreamGenericEmission? Generic { get; init; }
}
