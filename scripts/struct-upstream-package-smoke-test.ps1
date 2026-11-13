param(
    [Parameter(Mandatory = $true)][string] $PackageVersion,
    [Parameter(Mandatory = $true)][string] $PackagesDir,
    [Parameter(Mandatory = $true)][string] $WorkingDir,
    [switch] $Aot
)

$ErrorActionPreference = 'Stop'
$feed = [System.Security.SecurityElement]::Escape((Resolve-Path $PackagesDir).Path)
$WorkingDir = [IO.Path]::GetFullPath($WorkingDir)
New-Item -ItemType Directory -Path $WorkingDir -Force | Out-Null
$project = Join-Path $WorkingDir 'StructUpstreamSmoke.csproj'
$config = Join-Path $WorkingDir 'NuGet.Config'
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local">
      <package pattern="Zendiator" />
      <package pattern="Zendiator.Abstractions" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding UTF8
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><IsAotCompatible>true</IsAotCompatible><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="Zendiator" Version="$PackageVersion" /><PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
@'
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;
namespace StructPackage;
public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection(); services.AddSingleton<Probe>();
        services.AddZendiator<App>(c => { c.Handlers<Items>(DispatchFamily.Stream, typeof(Handler)); c.Pipeline<Items>(DispatchFamily.Stream, Stage.Wrapper<Relay>()); });
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var entry = scope.ServiceProvider.GetRequiredService<global::StructPackage.Generated.App.IZendiator>(); var p = provider.GetRequiredService<Probe>();
        var values = new List<int>(); await foreach (var value in entry.StreamAsync(new Items())) values.Add(value);
        var sink = new Sink(); await entry.StreamSource(new Items()).ConsumeAsync(sink);
        if (!values.SequenceEqual(new[] { 0, 1 }) || sink.Sum != 1 || p.Generic != 2 || p.Legacy != 0 || p.Closed != 2)
            throw new InvalidOperationException("Struct upstream package contract failed.");
        IStreamMiddleware<Items, int> standard = scope.ServiceProvider.GetRequiredService<Relay>();
        await using (var cursor = await standard.OpenAsync(new Items(), (IStreamUpstream<Items, int>)new DirectUpstream(p), default))
        {
            if (!await cursor.MoveNextAsync() || cursor.Current != 0)
                throw new InvalidOperationException("Legacy caller did not reach the standard typed implementation.");
        }
        IStreamMiddleware<Items, int> legacy = new LegacyRelay(p);
        await using (var cursor = await legacy.OpenAsync(new Items(), new DirectUpstream(p), default))
        {
            if (!await cursor.MoveNextAsync() || cursor.Current != 0)
                throw new InvalidOperationException("Typed caller did not reach the legacy implementation.");
        }
        if (p.Generic != 3 || p.Legacy != 1 || p.Closed != 4)
            throw new InvalidOperationException("Standard/legacy adapter calls or cleanup differ.");
        VerifyEagerDependencies();
        await VerifyCarrierDependenciesAsync();
        Console.WriteLine("STRUCT_UPSTREAM_PACKAGE_OK");
    }

    private static void VerifyEagerDependencies()
    {
        var probe = new EagerProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddZendiator<EagerApp>(configuration =>
        {
            configuration.Handlers<EagerRequest>(DispatchFamily.SendSync, typeof(EagerHandler));
        });
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            if (probe.Constructed != 0 || probe.Released != 0 || probe.Calls != 0)
                throw new InvalidOperationException("Eager dependency activated before entry resolution.");
            using (var unused = provider.CreateScope())
            {
                _ = unused.ServiceProvider.GetRequiredService<global::StructPackage.Generated.EagerApp.IZendiator>();
                if (probe.Constructed != 1 || probe.Released != 0 || probe.Calls != 0)
                    throw new InvalidOperationException("Unused eager scope did not activate exactly one selected dependency.");
            }
            if (probe.Constructed != 1 || probe.Released != 1 || probe.Calls != 0)
                throw new InvalidOperationException("Native DI did not release the unused eager dependency exactly once.");
            using (var used = provider.CreateScope())
            {
                var entry = used.ServiceProvider.GetRequiredService<global::StructPackage.Generated.EagerApp.IZendiator>();
                if (probe.Constructed != 2 || probe.Released != 1 || probe.Calls != 0)
                    throw new InvalidOperationException("Second eager scope did not receive its own selected dependency.");
                if (entry.Send(new EagerRequest(40)) != 42 || entry.Send(new EagerRequest(41)) != 43)
                    throw new InvalidOperationException("Repeated Send did not reuse the same eager handler.");
                if (probe.Constructed != 2 || probe.Released != 1 || probe.Calls != 2)
                    throw new InvalidOperationException("Repeated Send changed eager activation or ownership.");
            }
            if (probe.Constructed != 2 || probe.Released != 2 || probe.Calls != 2)
                throw new InvalidOperationException("Native DI did not release the used eager dependency exactly once.");
        }
        if (probe.Constructed != 2 || probe.Released != 2 || probe.Calls != 2)
            throw new InvalidOperationException("Provider disposal changed scoped eager ownership.");
        Console.WriteLine("EAGER_DEPENDENCY_PACKAGE_OK");
    }

    private static async Task VerifyCarrierDependenciesAsync()
    {
        var probe = new CarrierProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddZendiator<CarrierApp>(configuration =>
        {
            configuration.Handlers<CarrierRequest>(DispatchFamily.SendAsync, typeof(CarrierHandler));
            configuration.Pipeline<CarrierRequest>(DispatchFamily.SendAsync, Stage.Hook<CarrierBeforeOne>(), Stage.Hook<CarrierBeforeTwo>());
        });
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        using (var provider = services.BuildServiceProvider(options))
        {
            probe.Verify(0, 0, 0);
            using (var scope = provider.CreateScope())
            {
                var entry = scope.ServiceProvider.GetRequiredService<global::StructPackage.Generated.CarrierApp.IZendiator>();
                probe.Verify(0, 0, 0);
                if (await entry.SendAsync(new CarrierRequest(40)) != 43 || await entry.SendAsync(new CarrierRequest(41)) != 45)
                    throw new InvalidOperationException("Carrier Send results or receiver reuse differ.");
                probe.Verify(1, 0, 2);
            }
            probe.Verify(1, 1, 2);
        }
        probe.Verify(1, 1, 2);

        services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(CarrierHandler)));
        var rejected = false;
        try
        {
            using var invalid = services.BuildServiceProvider(options);
        }
        catch (AggregateException)
        {
            rejected = true;
        }
        if (!rejected)
            throw new InvalidOperationException("ValidateOnBuild did not reject the missing closed carrier receiver.");
        probe.Verify(1, 1, 2);
        Console.WriteLine("CARRIER_DEPENDENCY_PACKAGE_OK");
    }
}
[ZendiatorComposition("struct-package")] public sealed class App;
public sealed record Items : IStreamRequest<int>;
public sealed class Probe { public int Generic, Legacy, Closed; }
[Handler] internal sealed class Handler(Probe p) : IStreamSourceHandler<Items, int, Leaf>
{
    public ValueTask<Leaf> OpenAsync(Items request, CancellationToken token) => new(new Leaf(p, token));
}
internal struct Leaf(Probe p, CancellationToken token) : IAsyncEnumerator<int>
{
    private int index = -1;
    public int Current => index;
    public ValueTask<bool> MoveNextAsync() { token.ThrowIfCancellationRequested(); return new(++index < 2); }
    public ValueTask DisposeAsync() { p.Closed++; return default; }
}
public sealed class Relay(Probe p) : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync<TNext>(Items request, TNext next, CancellationToken token)
        where TNext : struct, IStreamUpstream<Items, int>
    { if (!typeof(TNext).IsValueType) throw new InvalidOperationException("Expected value port."); p.Generic++; return next.OpenAsync(request, token); }
}
public readonly struct DirectUpstream(Probe p) : IStreamUpstream<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, CancellationToken token) => new(new Leaf(p, token));
}
public sealed class LegacyRelay(Probe p) : IStreamMiddleware<Items, int>
{
    public ValueTask<IAsyncEnumerator<int>> OpenAsync(Items request, IStreamUpstream<Items, int> next, CancellationToken token)
    { p.Legacy++; return next.OpenAsync(request, token); }
}
public sealed class Sink : IStreamSink<int>
{
    public int Sum;
    public ValueTask<bool> OnNextAsync(int item, CancellationToken token) { Sum += item; return new(true); }
}

[ZendiatorComposition("eager-package", EagerDependencies = new[] { typeof(EagerHandler) })]
public sealed class EagerApp;

public readonly record struct EagerRequest(int Value) : ISyncRequest<int>;

public sealed class EagerProbe
{
    public int Constructed;
    public int Released;
    public int Calls;
}

[Handler]
public sealed class EagerHandler : ISyncRequestHandler<EagerRequest, int>, IDisposable
{
    private readonly EagerProbe probe;
    private readonly int instance;

    public EagerHandler(EagerProbe probe)
    {
        this.probe = probe;
        instance = ++probe.Constructed;
    }

    public int Handle(EagerRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        probe.Calls++;
        return request.Value + instance;
    }

    public void Dispose()
    {
        probe.Released++;
    }
}

[ZendiatorComposition("carrier-package")]
public sealed class CarrierApp;

public sealed record CarrierRequest(int Value) : IRequest<int>;

public sealed class CarrierProbe
{
    private readonly object?[] receivers = new object?[3];
    private readonly int[] constructed = new int[3], released = new int[3], calls = new int[3];

    public void Construct(int role, object receiver)
    {
        receivers[role] ??= receiver;
        constructed[role]++;
    }

    public int Call(int role, object receiver)
    {
        if (constructed.Any(count => count != 1) || !ReferenceEquals(receivers[role], receiver))
            throw new InvalidOperationException("All carrier receivers must be constructed before the first Before and reused on warm calls.");
        return ++calls[role];
    }

    public void Release(int role, object receiver)
    {
        if (!ReferenceEquals(receivers[role], receiver))
            throw new InvalidOperationException("DI released a different carrier receiver.");
        released[role]++;
    }

    public void Verify(int constructionCount, int releaseCount, int callCount)
    {
        if (constructed.Any(count => count != constructionCount) || released.Any(count => count != releaseCount) || calls.Any(count => count != callCount))
            throw new InvalidOperationException("Carrier activation, warm reuse or DI ownership differ.");
    }
}

public sealed class CarrierBeforeOne : ISendRequestTransform<CarrierRequest>, IDisposable
{
    private readonly CarrierProbe probe;
    public CarrierBeforeOne(CarrierProbe probe) { this.probe = probe; probe.Construct(0, this); }
    public CarrierRequest Before(CarrierRequest request, CancellationToken token)
    {
        probe.Call(0, this);
        return request with { Value = request.Value + 1 };
    }
    public void Dispose() => probe.Release(0, this);
}

public sealed class CarrierBeforeTwo : ISendRequestTransform<CarrierRequest>, IDisposable
{
    private readonly CarrierProbe probe;
    public CarrierBeforeTwo(CarrierProbe probe) { this.probe = probe; probe.Construct(1, this); }
    public CarrierRequest Before(CarrierRequest request, CancellationToken token)
    {
        probe.Call(1, this);
        return request with { Value = request.Value + 1 };
    }
    public void Dispose() => probe.Release(1, this);
}

[Handler]
public sealed class CarrierHandler : IRequestHandler<CarrierRequest, int>, IDisposable
{
    private readonly CarrierProbe probe;
    public CarrierHandler(CarrierProbe probe) { this.probe = probe; probe.Construct(2, this); }
    public ValueTask<int> HandleAsync(CarrierRequest request, CancellationToken token) => new(request.Value + probe.Call(2, this));
    public void Dispose() => probe.Release(2, this);
}

'@ | Set-Content -LiteralPath (Join-Path $WorkingDir 'Program.cs') -Encoding UTF8

$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $WorkingDir 'global-packages'
try {
    dotnet build $project -c Release --configfile $config -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Struct upstream package build failed.' }
    $managed = @(dotnet (Join-Path $WorkingDir 'bin/Release/net10.0/StructUpstreamSmoke.dll'))
    if ($LASTEXITCODE -ne 0 -or $managed -notcontains 'STRUCT_UPSTREAM_PACKAGE_OK' -or $managed -notcontains 'EAGER_DEPENDENCY_PACKAGE_OK' -or $managed -notcontains 'CARRIER_DEPENDENCY_PACKAGE_OK') { throw 'Struct upstream package run failed.' }
    $managed
    if ($Aot) {
        dotnet publish $project -c Release -r win-x64 -p:PublishAot=true --self-contained --configfile $config -o (Join-Path $WorkingDir 'native') -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Struct upstream package AOT publish failed.' }
        $native = @(& (Join-Path $WorkingDir 'native/StructUpstreamSmoke.exe'))
        if ($LASTEXITCODE -ne 0 -or $native -notcontains 'STRUCT_UPSTREAM_PACKAGE_OK' -or $native -notcontains 'EAGER_DEPENDENCY_PACKAGE_OK' -or $native -notcontains 'CARRIER_DEPENDENCY_PACKAGE_OK') { throw 'Struct upstream package AOT run failed.' }
        $native
    }
} finally {
    $env:NUGET_PACKAGES = $previousPackages
}
