namespace Zendiator.Sample.Contracts;

/// <summary>Collects onboarding quotes from every vendor.</summary>
public sealed record GetHouseholdQuotes(string ProductCode) : IMultiRequest<HouseholdQuote>;
