namespace Zendiator.Benchmarks;

public static class DrEventCounters
{
    public static long H1;
    public static long H4;

    public static void Reset() => H1 = H4 = 0;
}
