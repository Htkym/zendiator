using Microsoft.Extensions.DependencyInjection;

namespace Competitive.FoundatioRelease;

// Reuses the existing FirstSend boundary: scope creation and disposal are outside timing.
public sealed class FoundatioFreshScopes
{
    public const int Count = 16384;
    private IServiceScope[] _fresh = [];
    private int _used;
    private int _fullIterations;
    private bool _started;

    public void Prepare(ServiceProvider provider)
    {
        if (_fresh.Length != Count)
            _fresh = new IServiceScope[Count];
        for (int i = 0; i < _fresh.Length; i++)
            _fresh[i] = provider.CreateScope();
        _used = 0;
        _started = true;
    }

    public IServiceScope Take() => _fresh[_used++];

    public void Release()
    {
        int count = _used;
        foreach (var scope in _fresh)
            scope.Dispose();
        Array.Clear(_fresh);
        if (count == Count)
        {
            _fullIterations++;
            return;
        }
        if ((count == 1 || count == 16) && _fullIterations == 0 && IsJitProbe())
            return;
        throw new InvalidOperationException($"FirstSend used {count} of {Count} fresh scopes outside BDN JIT");
    }

    public void VerifyCompleted()
    {
        if (_started && _fullIterations == 0)
            throw new InvalidOperationException("FirstSend had no complete iteration");
    }

    private static bool IsJitProbe()
    {
        foreach (var frame in new System.Diagnostics.StackTrace().GetFrames() ?? [])
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType?.FullName == "BenchmarkDotNet.Engines.EngineFactory" && method.Name == "Jit")
                return true;
        }
        return false;
    }
}
