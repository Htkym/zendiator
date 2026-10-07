namespace Zendiator.DependencyInjection;

// Only validator configurations use this carrier. Reusing the existing behaviors reference
// preserves the no-validator recorder and snapshot instance layouts and allocations.
internal sealed class StreamValidatorRegistrations(
    List<(string BehaviorType, int Order)> behaviors,
    List<(string ValidatorType, int Order)> validators) : List<(string BehaviorType, int Order)>(behaviors)
{
    internal List<(string ValidatorType, int Order)> Validators { get; } = validators;
}
