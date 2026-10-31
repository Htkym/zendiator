[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SourceCommit
)

$ErrorActionPreference = 'Stop'
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Expected a full source commit SHA.' }

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
$files = foreach ($id in @('Zendiator.Abstractions', 'Zendiator', 'Zendiator.CodeFixes')) {
  $extensions = if ($id -eq 'Zendiator.CodeFixes') { @('nupkg') } else { @('nupkg', 'snupkg') }
  foreach ($extension in $extensions) {
    $file = Get-Item "artifacts/packages/$id.$version.$extension"
    $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
      $entries = @($archive.Entries.FullName)
      foreach ($required in @("$id.nuspec", '[Content_Types].xml', '_rels/.rels')) {
        if ($entries -notcontains $required) { throw "Missing $required in $file" }
      }
      if ($extension -eq 'nupkg') {
        $requiredAssets = if ($id -eq 'Zendiator.CodeFixes') {
          @('README.md', 'analyzers/dotnet/cs/Zendiator.CodeFixes.dll')
        } else { @('README.md', "lib/net10.0/$id.dll", "lib/net10.0/$id.xml") }
        foreach ($required in $requiredAssets) {
          if ($entries -notcontains $required) { throw "Missing $required in $file" }
        }
        if ($id -eq 'Zendiator') {
          foreach ($required in @('analyzers/dotnet/cs/Zendiator.SourceGenerator.dll', 'buildTransitive/Zendiator.props')) {
            if ($entries -notcontains $required) { throw "Missing $required in $file" }
          }
        }
      }
      if ($entries -match '(?i)(Microsoft.CodeAnalysis|MessagePipe|(^|/)(tests|benchmarks|\.local|temp)/)') {
        throw "Unexpected package asset in $file"
      }
      $allowedEntries = @("$id.nuspec", '[Content_Types].xml', '_rels/.rels', 'package/services/metadata/core-properties/nuget.psmdcp')
      if ($extension -eq 'nupkg') {
        if ($id -eq 'Zendiator.CodeFixes') {
          $allowedEntries += @('README.md', 'analyzers/dotnet/cs/Zendiator.CodeFixes.dll')
        } else { $allowedEntries += @('README.md', 'icon.png', "lib/net10.0/$id.dll", "lib/net10.0/$id.xml") }
        if ($id -eq 'Zendiator') { $allowedEntries += @('analyzers/dotnet/cs/Zendiator.SourceGenerator.dll', 'buildTransitive/Zendiator.props') }
      } else { $allowedEntries += "lib/net10.0/$id.pdb" }
      $normalizedEntries = $entries | ForEach-Object {
        if ($_ -match '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$') { 'package/services/metadata/core-properties/nuget.psmdcp' } else { $_ }
      }
      if (Compare-Object ($normalizedEntries | Sort-Object) ($allowedEntries | Sort-Object)) { throw "Unexpected package entry set in $file" }
      if ($extension -eq 'snupkg') {
        $pdbStream = [IO.MemoryStream]::new()
        try {
          $entryStream = $archive.GetEntry("lib/net10.0/$id.pdb").Open()
          try { $entryStream.CopyTo($pdbStream) } finally { $entryStream.Dispose() }
          $pdbStream.Position = 0
          $pdb = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream, [Reflection.Metadata.MetadataStreamOptions]::LeaveOpen, 0)
          try {
            $pdbReader = $pdb.GetMetadataReader([Reflection.Metadata.MetadataReaderOptions]::Default, $null)
            foreach ($document in $pdbReader.Documents) {
              $documentName = $pdbReader.GetString($pdbReader.GetDocument($document).Name)
              if ($documentName -match '^(?:[A-Za-z]:[\\/]|\\\\|/home/|/Users/)') { throw "Machine-local symbol path in $file" }
            }
          } finally { $pdb.Dispose() }
        } finally { $pdbStream.Dispose() }
      }
      $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
      try { $metadata = ([xml]$reader.ReadToEnd()).package.metadata } finally { $reader.Dispose() }
      if ($metadata.version -ne $version -or $metadata.repository.commit -ne $SourceCommit) {
        throw "Package version/source mismatch in $file"
      }
      if ($extension -eq 'nupkg' -and $id -ne 'Zendiator.CodeFixes' -and $metadata.icon -ne 'icon.png') {
        throw "Missing package icon metadata in $file"
      }
      if ($id -eq 'Zendiator.CodeFixes') {
        if ($metadata.developmentDependency -ne 'true' -or $metadata.readme -ne 'README.md' -or
            $metadata.SelectNodes(".//*[local-name()='dependency']").Count -ne 0) {
          throw 'CodeFix package metadata is invalid'
        }
        $readme = [IO.MemoryStream]::new()
        try {
          $stream = $archive.GetEntry('README.md').Open()
          try { $stream.CopyTo($readme) } finally { $stream.Dispose() }
          $readme.Position = 0
          if ((Get-FileHash -InputStream $readme -Algorithm SHA256).Hash -ne
              (Get-FileHash 'src/Zendiator.CodeFixes/README.md' -Algorithm SHA256).Hash) {
            throw 'CodeFix package readme differs from source'
          }
        } finally { $readme.Dispose() }
      }
      $dependencies = @($metadata.dependencies.group.dependency | Where-Object { $_ })
      $allowed = if ($id -eq 'Zendiator') { @('Zendiator.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions') } else { @() }
      if (@($dependencies).Count -ne $allowed.Count -or @($dependencies | Where-Object { $_.id -notin $allowed }).Count) {
        throw "Unexpected package dependencies in $file"
      }
      foreach ($dependency in $dependencies) {
        $expectedVersion = if ($dependency.id -eq 'Zendiator.Abstractions') { $version } else { '10.0.0' }
        if ($dependency.version -ne $expectedVersion) { throw "Unexpected dependency version in $file" }
      }
    } finally { $archive.Dispose() }
    [ordered]@{ Name = $file.Name; Size = $file.Length; SHA256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash; Dependencies = @($dependencies | ForEach-Object { [ordered]@{ Id = $_.id; Version = $_.version } }) }
  }
}
$sourcePaths = git ls-files -- 'src/**/*.cs'
if ($LASTEXITCODE -ne 0) { throw 'Failed to enumerate package sources.' }
$sources = foreach ($path in ($sourcePaths | Sort-Object)) {
  [ordered]@{ Path = $path; SHA256 = (Get-FileHash $path -Algorithm SHA256).Hash }
}
[ordered]@{ Version = $version; SourceCommit = $SourceCommit; Sources = @($sources); Build = [ordered]@{ Job = $env:GITHUB_JOB; Run = $env:GITHUB_RUN_ID; Attempt = $env:GITHUB_RUN_ATTEMPT }; Verification = 'Pending'; Files = @($files) } |
  ConvertTo-Json -Depth 6 | Set-Content artifacts/packages/manifest.json
