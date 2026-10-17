using System.Collections.ObjectModel;

namespace Zendiator.DependencyInjection;

internal sealed class StreamValidatorRegistrations(
    List<(string BehaviorType, int Order)> behaviors,
    List<(string ValidatorType, int Order)> validators) : List<(string BehaviorType, int Order)>(behaviors)
{
    internal List<(string ValidatorType, int Order)> Validators { get; } = validators;
}

internal sealed class ReadOnlyStreamValidatorRegistrations(
    List<(string BehaviorType, int Order)> behaviors,
    List<(string ValidatorType, int Order)> validators) : ReadOnlyCollection<(string BehaviorType, int Order)>(behaviors)
{
    internal IReadOnlyList<(string ValidatorType, int Order)> Validators { get; } = validators.AsReadOnly();
}
