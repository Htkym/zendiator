$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$out = Join-Path $env:RUNNER_TEMP 'control-format-output'
$baseline = Join-Path $env:RUNNER_TEMP 'control-format-baseline'
New-Item -ItemType Directory -Force $out, $baseline | Out-Null
$tracked = @(git ls-files -- '*.cs')
if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) { throw 'Unable to enumerate tracked C# sources' }
foreach ($file in $tracked) {
    $destination = Join-Path $baseline $file
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item $file $destination
}
$verifier = Join-Path $env:RUNNER_TEMP 'control-format-verifier'
Copy-Item 'scripts/FormatTokenCheck' $verifier -Recurse
$verifierProject = Join-Path $verifier 'FormatTokenCheck.csproj'
dotnet build $verifierProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Token verifier build failed' }
$verifierDll = Join-Path $verifier 'bin/Release/net10.0/FormatTokenCheck.dll'
function Check-Tokens([string] $name) {
    dotnet $verifierDll $baseline $root (Join-Path $out "$name.json")
    if ($LASTEXITCODE -ne 0) { throw "$name token check failed" }
}
$pilot = 'src/Zendiator.SourceGenerator/Emission/SourceEmitter.cs'
$pilotBefore = (Get-FileHash $pilot -Algorithm SHA256).Hash
dotnet format style src/Zendiator.SourceGenerator/Zendiator.SourceGenerator.csproj --diagnostics IDE2001 --include $pilot --no-restore --verbosity normal
if ($LASTEXITCODE -ne 0) { throw 'IDE2001 pilot failed' }
if ($pilotBefore -eq (Get-FileHash $pilot -Algorithm SHA256).Hash) { throw 'Pilot did not change: IDE2001 effectiveness not established' }
Check-Tokens 'pilot-token-check'
[xml]$solution = Get-Content 'Zendiator.slnx' -Raw
$projects = @($solution.SelectNodes('//Project') | ForEach-Object { $_.Path } | Where-Object { $_ -like '*.csproj' })
if ($projects.Count -eq 0) { throw 'No C# projects in solution' }
foreach ($project in $projects) {
    if (-not (Test-Path $project)) { throw "Missing solution project: $project" }
}
foreach ($project in $projects) {
    dotnet format style $project --diagnostics IDE2001 --include . --no-restore --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw "IDE2001 formatting failed: $project" }
}
Check-Tokens 'all-source-token-check'
foreach ($project in $projects) {
    dotnet format style $project --diagnostics IDE2001 --include . --no-restore --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw "IDE2001 verification failed: $project" }
}
$changed = @(git diff --name-only)
if ($LASTEXITCODE -ne 0) { throw 'git diff failed' }
if (@($changed | Where-Object { $_ -notmatch '\.cs$' }).Count -gt 0) { throw 'Unexpected changed paths' }
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed' }
git diff --binary --output="$out/control-format.patch" -- '*.cs'
if ($LASTEXITCODE -ne 0) { throw 'Patch generation failed' }
[ordered]@{ sourceCommit = $env:GITHUB_SHA; sdk = (dotnet --version); completed = $true; changedFiles = $changed; scope = 'IDE2001 only; all solution C# projects, including tests/samples/benchmarks'; tests = 'Not run: this job produces a reviewed formatting proposal, not release acceptance' } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'summary.json')
"IDE2001 proposal: $($changed.Count) changed source files; token checks passed; no commit or push." >> $env:GITHUB_STEP_SUMMARY
