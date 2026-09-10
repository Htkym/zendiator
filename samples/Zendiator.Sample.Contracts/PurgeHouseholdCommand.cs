namespace Zendiator.Sample.Contracts;

/// <summary>Purges a household without a response value.</summary>
public sealed record PurgeHouseholdCommand(Guid Id) : ICommand;
