namespace Zendiator.Benchmarks;

public static class MoCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = 0;
}
