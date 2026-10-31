namespace Zendiator.Sample.Contracts;

public readonly record struct GetTargetYearQuery(int Year) : IQuery<MemorialTargetDto>;
