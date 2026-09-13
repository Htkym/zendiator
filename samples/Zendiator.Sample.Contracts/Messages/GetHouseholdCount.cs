namespace Zendiator.Sample.Contracts;

/// <summary>Counts households. Open over a marker type so callers keep their type arguments open.</summary>
public sealed record GetHouseholdCount<TMarker> : IRequest<int>;
