namespace Zendiator.Sample.Contracts;

/// <summary>Published after a household is purged.</summary>
public sealed record HouseholdPurged(Guid Id) : INotification;
