$ErrorActionPreference = 'Stop'
[xml]$solution = Get-Content 'Zendiator.slnx' -Raw
$solutionProjects = @($solution.SelectNodes('//Project') | ForEach-Object { $_.Path })
$projects = @(git ls-files -- '*.csproj' | Where-Object { $_ -notlike 'scripts/FormatTokenCheck/*' })
if ($LASTEXITCODE -ne 0) { throw 'Project enumeration failed' }
$extraProjects = @($projects | Where-Object { $_ -notin $solutionProjects })
$feed = Join-Path $env:RUNNER_TEMP 'format-local-feed'
$packages = Join-Path $env:RUNNER_TEMP 'format-consumer-packages'
New-Item -ItemType Directory -Force $feed, $packages | Out-Null
# Local, unpublished packages preserve the samples' declared version and PackageReference usage.
foreach ($packageProject in @('src/Zendiator.Abstractions/Zendiator.Abstractions.csproj', 'src/Zendiator/Zendiator.csproj')) {
    dotnet pack $packageProject -c Release --no-restore -o $feed
    if ($LASTEXITCODE -ne 0) { throw "Local package preparation failed: $packageProject" }
}
foreach ($project in $extraProjects) {
    [xml]$xml = Get-Content $project -Raw
    $consumesPackage = @($xml.SelectNodes('//PackageReference') | Where-Object { $_.Include -eq 'Zendiator' }).Count -gt 0
    if ($consumesPackage) {
        dotnet restore $project --packages $packages --source $feed --source https://api.nuget.org/v3/index.json
    } else {
        dotnet restore $project
    }
    if ($LASTEXITCODE -ne 0) { throw "Additional project restore failed: $project" }
}
"Formatting preparation covers $($projects.Count) tracked C# projects. Temporary proposal branch skips ordinary CI only here; final PR retains all normal CI checks. No package publication or benchmark execution." >> $env:GITHUB_STEP_SUMMARY
