namespace Zendiator;

/// <summary>
/// Experimental. Opts a supported private asynchronous producer method into
/// generated pooled execution at its explicit source call sites.
/// </summary>
/// <remarks>
/// Supported method bodies and diagnostics may change in 1.x.
/// The initial supported shape is a parameterless instance async ValueTask&lt;bool&gt;
/// method in a top-level non-generic partial class. Unsupported source forms are
/// reported as generator diagnostics. Each returned ValueTask must be consumed
/// exactly once. Generated implementation names and exception stack traces can differ.
/// This attribute does not pool the cursor or change async iterator methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GeneratePooledProducerAttribute : Attribute;
