namespace Zendiator.Sample.Contracts;

/// <summary>Registers a household. Returns a user-owned result instead of throwing.</summary>
public readonly record struct RegisterHouseholdCommand(string Name)
    : ICommand<Result<HouseholdId, ValidationError>>;
