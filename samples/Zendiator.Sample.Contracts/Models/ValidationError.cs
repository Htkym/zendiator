namespace Zendiator.Sample.Contracts;

public sealed record ValidationError(string Field, string Message);
