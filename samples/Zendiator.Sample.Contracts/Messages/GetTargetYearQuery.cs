namespace Zendiator.Sample.Contracts;

/// <summary>Queries memorial targets for a year. Struct to avoid allocation.</summary>
public readonly record struct GetTargetYearQuery(int Year) : IQuery<MemorialTargetDto>;
