param(
    [ValidateSet('all', 'send', 'features', 'streams', 'smoke')]
    [string]$Group = 'all',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Join-Path $PSScriptRoot 'Zendiator.UseCaseBenchmarks'
$project = Join-Path $projectDirectory 'Zendiator.UseCaseBenchmarks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repo ('.local/benchmarks/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Get-SourceDigest {
    $files = @(
        Get-ChildItem -LiteralPath (Join-Path $repo 'src') -File -Recurse
        Get-ChildItem -LiteralPath $PSScriptRoot -File
        Get-ChildItem -LiteralPath $projectDirectory -File -Recurse
    ) | Where-Object {
        $_.Extension -in '.cs', '.csproj', '.ps1', '.py', '.json', '.props', '.editorconfig' -and
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } | Sort-Object FullName
    $lines = $files | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

$revision = (git -C $repo rev-parse HEAD).Trim()
$sourceBefore = Get-SourceDigest
dotnet restore $project --locked-mode *> (Join-Path $output 'restore.log')
if ($LASTEXITCODE -ne 0) { throw "Restore failed; see $output/restore.log" }
dotnet build $project -c Release --no-restore *> (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Build failed; see $output/build.log" }

$groups = if ($Group -eq 'all') { @('send', 'features', 'streams') } else { @($Group) }
$saved = @{}
foreach ($key in 'COLD_RUN', 'COLD_MATRIX_FILE', 'COLD_CASE', 'COLD_FORMAL', 'COLD_PINNED', 'COLD_EXPECT_CHILD_Z_SHA') {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
$productHash = $null
try {
    foreach ($name in $groups) {
        $matrix = Join-Path $projectDirectory "$name.json"
        $expected = @(Get-Content -LiteralPath $matrix -Raw | ConvertFrom-Json).Count
        $run = Join-Path $output "runs/$name"
        New-Item -ItemType Directory -Path $run -Force | Out-Null
        $env:COLD_RUN = $run
        $env:COLD_MATRIX_FILE = $matrix
        $env:COLD_CASE = $null
        $env:COLD_FORMAL = $null
        $env:COLD_PINNED = '1'
        # The BDN child can have a different binary hash from the parent build.
        # Verify all child binaries against each other after the run instead.
        $env:COLD_EXPECT_CHILD_Z_SHA = 'SKIP'
        Push-Location $projectDirectory
        try {
            dotnet './bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll' --filter '*' *> (Join-Path $run 'run.log')
            $runExit = $LASTEXITCODE
        }
        finally { Pop-Location }
        $outcome = if (Test-Path -LiteralPath (Join-Path $run 'outcome.json')) {
            Get-Content -LiteralPath (Join-Path $run 'outcome.json') -Raw | ConvertFrom-Json
        } else { $null }
        $children = @(Get-ChildItem -LiteralPath $run -Filter 'child-*.json')
        $hashes = @($children | ForEach-Object {
            ((Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).assemblies |
                Where-Object name -eq 'Zendiator').sha256
        } | Sort-Object -Unique)
        [pscustomobject]@{
            group = $name; revision = $revision; expectedCases = $expected
            exitCode = $runExit; outcome = $outcome; childCount = $children.Count
            productHashes = $hashes; sourceDigest = $sourceBefore
            profile = 'Release; affinity 1; 20 warmup; 12 measurement; 500 ms requested iteration; 1 launch'
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'run-info.json')
        if ($runExit -ne 0 -or $null -eq $outcome -or $outcome.cases -ne $expected -or
            $outcome.failures -ne 0 -or $outcome.validationErrors -ne 0 -or
            $children.Count -ne $expected -or $hashes.Count -ne 1 -or
            ($productHash -and $hashes[0] -ne $productHash)) {
            throw "Invalid $name run; see $run"
        }
        $productHash = $hashes[0]
        Write-Output "$name passed: $expected cases; product DLL $productHash"
    }
}
finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
}

if ($sourceBefore -ne (Get-SourceDigest) -or $revision -ne (git -C $repo rev-parse HEAD).Trim()) {
    throw "Source changed during measurement; see $output"
}
if ($Group -ne 'smoke') {
    python (Join-Path $projectDirectory 'analyze.py') $output
    if ($LASTEXITCODE -ne 0) { throw "Analysis failed; see $output" }
}
if ($Group -eq 'all') {
    python (Join-Path $projectDirectory 'make_report.py') $output
    if ($LASTEXITCODE -ne 0) { throw "Report generation failed; see $output" }
}
Write-Output $output
