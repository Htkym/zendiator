namespace Zendiator.Generator.Tests;

internal sealed record StreamSeedCase(string Id, uint Seed, int GeneratorVersion, int DrawOffset, int DrawCount,
    int[] Input, string? FailureStage, string? FailureType);

internal static class CanonicalStreamSeedCorpus
{
    public const uint Seed = 0xC0800500u;
    public const int Version = 1;
    public const int Count = 8;
    public static StreamSeedCase[] Create()
    {
        var state = Seed;
        int[] Draw(int length)
        {
            var input = new int[length];
            for (var i = 0; i < length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                input[i] = (int)(state % 9) - 4;
            }
            return input;
        }
        // One PRNG stream: seed-4 consumes draws0..3; seed-16 consumes draws4..19.
        var four = Draw(4); var sixteen = Draw(16);
        return
        [
            new("empty", Seed, Version, 0, 0, [], null, null),
            new("filtered", Seed, Version, 0, 0, [-1, 0], null, null),
            new("single", Seed, Version, 0, 0, [1], null, null),
            new("seed-4", Seed, Version, 0, 4, four, null, null),
            new("seed-16", Seed, Version, 4, 16, sixteen, null, null),
            new("overflow", Seed, Version, 0, 0, [int.MaxValue], "Map", "System.OverflowException"),
            new("filter-failure", Seed, Version, 0, 0, [1], "F1", "System.FormatException"),
            new("factory-failure", Seed, Version, 0, 0, [1], "F1.factory", "System.InvalidOperationException")
        ];
    }
}
