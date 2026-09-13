namespace Zendiator.Sample.Contracts;

/// <summary>Parses a year from UTF-8 bytes without allocating.</summary>
public readonly ref struct ParseYear : ISyncRequest<int>
{
    public ParseYear(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}
