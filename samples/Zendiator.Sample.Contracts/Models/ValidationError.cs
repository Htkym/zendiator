namespace Zendiator.Sample.Contracts;

/// <summary>Validation failure details.</summary>
public sealed record ValidationError(string Field, string Message);
