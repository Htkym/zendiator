<#
.SYNOPSIS
  Packs Release outputs, compares the runtime and contracts to the preceding release,
  and runs that release's sample before and after a PackageReference upgrade.
.DESCRIPTION
  Run after a Release build. Each tagged release is checked against its predecessor.
  Subsequent changes use that release as their baseline, even before its packages
  are available on a public feed or the development version has been bumped.
#>
[CmdletBinding()]
param(
    [string]$PackagesDir = (Join-Path $PSScriptRoot '../artifacts/packages'),
    [string]$ScratchRoot = [IO.Path]::GetTempPath(),
    [string]$DependencySource = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = ([xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version
$startingPoint = if (git -C $repo tag --points-at HEAD --list "v$version") { 'HEAD^' } else { 'HEAD' }
$tag = git -C $repo describe --first-parent --tags --abbrev=0 --match 'v[0-9]*' $startingPoint
if ($LASTEXITCODE -ne 0) { throw 'Fetch the release tags and their history before checking package compatibility.' }
$baseline = $tag.Substring(1)
$commit = git -C $repo rev-parse --verify "${tag}^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Cannot resolve $tag." }
$PackagesDir = (New-Item -ItemType Directory -Force $PackagesDir).FullName
$parent = (New-Item -ItemType Directory -Force $ScratchRoot).FullName
$scratch = (New-Item -ItemType Directory (Join-Path $parent ('zendiator-upgrade-' + [guid]::NewGuid().ToString('N')))).FullName
$source = Join-Path $scratch 'source'
$feed = Join-Path $scratch 'feed'
$config = Join-Path $scratch 'NuGet.Config'
Write-Host "Comparing $version with $tag ($commit), using $scratch"

function Invoke-Sample([string]$output, [string]$scenario) {
    $lines = @(dotnet (Join-Path $output 'Zendiator.Sample.Host.dll'))
    $notifications = @($lines -match 'Invalidated purge cache |Audited purge ')
    if ($LASTEXITCODE -ne 0 -or $lines -notcontains 'Year 2026: Household-2026-1, Household-2026-2' -or
        $notifications.Count -ne 2 -or $notifications[0] -notmatch 'Invalidated purge cache ' -or
        $notifications[1] -notmatch 'Audited purge ' -or
        (($lines -like 'Streamed Household-*') -join ',') -ne 'Streamed Household-0,Streamed Household-1,Streamed Household-2') {
        throw "Sample failed ($scenario): $($lines -join [Environment]::NewLine)"
    }
    Write-Host "Passed: $scenario"
}

$zip = Join-Path $scratch 'baseline.zip'
git -C $repo archive --format=zip "--output=$zip" $commit -- Directory.Build.props global.json README.md docs/icon src samples
if ($LASTEXITCODE -ne 0) { throw "Could not export $tag." }
Expand-Archive -LiteralPath $zip -DestinationPath $source
if (([xml](Get-Content -Raw "$source/Directory.Build.props")).Project.PropertyGroup.Version -ne $baseline) {
    throw "The package version in $tag does not match the release tag."
}

foreach ($id in 'Zendiator.Abstractions','Zendiator') {
    dotnet pack "$source/src/$id/$id.csproj" -c Release -o $feed "-p:RestoreSources=$DependencySource" -p:ContinuousIntegrationBuild=false
    if ($LASTEXITCODE -ne 0) { throw "Failed to pack $id from $tag." }

    dotnet pack "$repo/src/$id/$id.csproj" -c Release --no-build -o $PackagesDir `
        "-p:PackageValidationBaselineVersion=$baseline" "-p:PackageValidationBaselinePath=$feed/$id.$baseline.nupkg" `
        -p:ApiCompatEnableRuleCannotChangeParameterName=true
    if ($LASTEXITCODE -ne 0) { throw "Package compatibility failed for $id against $tag." }
}
dotnet pack "$repo/src/Zendiator.CodeFixes/Zendiator.CodeFixes.csproj" -c Release --no-build -o $PackagesDir
if ($LASTEXITCODE -ne 0) { throw 'CodeFix package creation failed.' }

if ($baseline -ne $version) {
    Copy-Item -LiteralPath "$PackagesDir/Zendiator.Abstractions.$version.nupkg" -Destination $feed
    & "$PSScriptRoot/Write-PackageSmokeConfig.ps1" -Path $config -PackagesDir $feed -DependencySource $DependencySource
    $mixedDir = (New-Item -ItemType Directory (Join-Path $scratch 'mixed')).FullName
    $mixedProject = Join-Path $mixedDir 'Mixed.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator" Version="[$baseline]" />
    <PackageReference Include="Zendiator.Abstractions" Version="[$version]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $mixedProject -Encoding utf8
    $mixed = @(dotnet build $mixedProject --configfile $config "-p:RestorePackagesPath=$scratch/mixed-packages" 2>&1)
    if ($LASTEXITCODE -eq 0 -or !($mixed -match 'Reference the same package version')) {
        throw "Expected a version mismatch from $baseline runtime and $version contracts: $($mixed -join [Environment]::NewLine)"
    }
    Write-Host 'Passed: a mixed runtime and contracts package produces the version diagnostic'
}

# Exercise the original Host, Application and Contracts sources using only shipped Zendiator assets.
@'
<Project>
  <ItemGroup>
    <ProjectReference Remove="../../src/Zendiator/Zendiator.csproj;../../src/Zendiator.Abstractions/Zendiator.Abstractions.csproj;../../src/Zendiator.SourceGenerator/Zendiator.SourceGenerator.csproj" />
    <PackageReference Include="Zendiator.Abstractions" Version="[$(ZendiatorPackageVersion)]" Condition="'$(MSBuildProjectName)' == 'Zendiator.Sample.Contracts'" />
    <PackageReference Include="Zendiator" Version="[$(ZendiatorPackageVersion)]" Condition="'$(MSBuildProjectName)' == 'Zendiator.Sample.Application'" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath "$source/samples/Directory.Build.targets" -Encoding utf8
$project = "$source/samples/Zendiator.Sample.Host/Zendiator.Sample.Host.csproj"
foreach ($case in @(@{ Name='previous'; Version=$baseline; Feed=$feed }, @{ Name='candidate'; Version=$version; Feed=$PackagesDir })) {
    $v = $case.Version
    $artifacts = Join-Path $scratch $case.Name
    & "$PSScriptRoot/Write-PackageSmokeConfig.ps1" -Path $config -PackagesDir $case.Feed -DependencySource $DependencySource
    dotnet build $project -c Release --artifacts-path $artifacts --configfile $config `
        "-p:RestorePackagesPath=$artifacts/packages" "-p:ZendiatorPackageVersion=$v" -p:ContinuousIntegrationBuild=false
    if ($LASTEXITCODE -ne 0) { throw "Cannot build the $tag sample against $v." }
    $output = "$artifacts/bin/Zendiator.Sample.Host/release"
    $libraries = (Get-Content -Raw "$output/Zendiator.Sample.Host.deps.json" | ConvertFrom-Json -AsHashtable).libraries
    foreach ($id in 'Zendiator', 'Zendiator.Abstractions') {
        if ($libraries["$id/$v"].type -ne 'package') { throw "Expected $id/$v from the isolated package feed." }
    }
    Invoke-Sample $output "source compiled against $v"
}
$old = "$scratch/previous/bin/Zendiator.Sample.Host/release"
$new = "$scratch/candidate/bin/Zendiator.Sample.Host/release"
Copy-Item -LiteralPath "$old/Zendiator.Sample.Host.dll" -Destination $new -Force
Invoke-Sample $new 'previously compiled host calls the regenerated facade'
Copy-Item -LiteralPath "$old/Zendiator.Sample.Application.dll","$old/Zendiator.Sample.Contracts.dll" -Destination $new -Force
Invoke-Sample $new 'previously compiled host, handlers and generated facade run on the new runtime'

if ((Split-Path -Parent (Resolve-Path -LiteralPath $scratch).Path) -ne $parent.TrimEnd('\','/')) {
    throw "Unexpected scratch location: $scratch"
}
Remove-Item -Recurse -Force -LiteralPath $scratch
