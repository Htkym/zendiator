namespace Zendiator.Sample.Contracts;

/// <summary>Streams household names without materializing the whole list.</summary>
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;
