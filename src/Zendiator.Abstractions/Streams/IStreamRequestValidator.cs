namespace Zendiator;

/// <summary>Validates a closed streaming request synchronously when StreamAsync is called.</summary>
/// <remarks>Validation runs once per call, outside the lazy pipeline. Implementations must be
/// lightweight, input-only and safe for concurrent calls. Do not perform asynchronous work or I/O.
/// Captured instances are reused within the mediator, including Transient registrations.
/// Keep the input stable and the DI scope valid until enumeration completes.</remarks>
public interface IStreamRequestValidator<in TRequest>
{
    /// <summary>Validates the request or throws synchronously before an enumerable is created.</summary>
    void Validate(TRequest request);
}
