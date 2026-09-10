using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Validates the command and returns a failure instead of throwing.</summary>
public sealed class RegisterHouseholdHandler : ICommandHandler<RegisterHouseholdCommand, Result<HouseholdId, ValidationError>>
{
    public ValueTask<Result<HouseholdId, ValidationError>> HandleAsync(
        RegisterHouseholdCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.Name))
            return new(Result<HouseholdId, ValidationError>.Failure(new ValidationError("Name", "Name must not be empty.")));

        return new(Result<HouseholdId, ValidationError>.Success(new HouseholdId(Guid.NewGuid())));
    }
}
