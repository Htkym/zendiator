using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.RScale.R1;

if (args.Contains("--retained"))
{
    var rounds = 3;
    var scopes = 2000;
    foreach (var a in args)
    {
        if (a.StartsWith("--rounds=", StringComparison.Ordinal) && int.TryParse(a["--rounds=".Length..], out var r))
            rounds = r;
        if (a.StartsWith("--scopes=", StringComparison.Ordinal) && int.TryParse(a["--scopes=".Length..], out var n))
            scopes = n;
    }

    Console.WriteLine("variant,R,N,totalBytes");
    for (var round = 0; round < rounds; round++)
    {
        Console.WriteLine($"U0,{RScaleExpected.RouteCount},{scopes},{RetainedHarness.Measure(scopes, static m => { m.GetHashCode(); })}");
        Console.WriteLine($"Warm0,{RScaleExpected.RouteCount},{scopes},{RetainedHarness.Measure(scopes, static m => { _ = m.SendAsync(new Q0(0)).GetAwaiter().GetResult(); _ = m.SendAsync(new Q0(0)).GetAwaiter().GetResult(); })}");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
