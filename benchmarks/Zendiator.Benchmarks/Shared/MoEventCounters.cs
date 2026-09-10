namespace Zendiator.Benchmarks;

public static class MoEventCounters
{
    public static long H1;
    public static long H4;
    public static long H16;

    public static void Reset() => H1 = H4 = H16 = 0;
}
