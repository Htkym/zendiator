param(
    [string]$Pattern = '*ResolveSlow*',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectDirectory = Join-Path $PSScriptRoot 'Zendiator.UseCaseBenchmarks'
$project = Join-Path $projectDirectory 'Zendiator.UseCaseBenchmarks.csproj'
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repo ('.local/benchmarks/jit-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
New-Item -ItemType Directory -Path $output -Force | Out-Null
dotnet restore $project --locked-mode *> (Join-Path $output 'restore.log')
if ($LASTEXITCODE -ne 0) { throw "Restore failed; see $output/restore.log" }
dotnet build $project -c Release --no-restore *> (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Build failed; see $output/build.log" }

$saved = @{}
foreach ($key in 'COLD_RUN', 'DOTNET_JitDisasm', 'DOTNET_TieredCompilation', 'DOTNET_TieredPGO') {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
try {
    $env:COLD_RUN = $output
    $env:DOTNET_JitDisasm = $Pattern
    $env:DOTNET_TieredCompilation = '1'
    $env:DOTNET_TieredPGO = '1'
    Push-Location $projectDirectory
    try {
        dotnet './bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll' --inspect *> (Join-Path $output 'jit.log')
        $runExit = $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
}
[pscustomobject]@{
    revision = (git -C $repo rev-parse HEAD).Trim()
    pattern = $Pattern
    exitCode = $runExit
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'run-info.json')
if ($runExit -ne 0 -or
    -not (Select-String -LiteralPath (Join-Path $output 'jit.log') -SimpleMatch 'JIT diagnostic passed.' -Quiet) -or
    -not (Select-String -LiteralPath (Join-Path $output 'jit.log') -SimpleMatch 'Tier1' -Quiet)) {
    throw "JIT inspection failed or never reached Tier1; see $output/jit.log"
}
Write-Output $output
