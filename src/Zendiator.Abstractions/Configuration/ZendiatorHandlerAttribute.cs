namespace Zendiator;

/// <summary>Explicitly identifies a source handler for the 0.5 generation contract.</summary>
/// <remarks>The new generation backend must support the selected contract. A marked handler
/// is never silently generated through the legacy discovery or execution contract.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ZendiatorHandlerAttribute : Attribute
{
    /// <summary>Lets a zero-stage Stream route return this handler's enumerable directly through the Mediator.</summary>
    /// <remarks>The handler owns token composition, independent enumerations, Current validity, concurrent-operation
    /// guards, and cursor cleanup. Handler resolution and HandleAsync run when StreamAsync is called.
    /// Staged or session-backed routes keep framework orchestration.</remarks>
    public bool OwnsStream { get; set; }
}
