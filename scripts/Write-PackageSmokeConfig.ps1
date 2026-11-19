[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Path,
    [Parameter(Mandatory)][string] $PackagesDir,
    [string] $DependencySource = 'https://api.nuget.org/v3/index.json'
)

$feed = [System.Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $PackagesDir).Path)
$dependencies = [System.Security.SecurityElement]::Escape($DependencySource)
@"
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$feed" />
    <add key="dependencies" value="$dependencies" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="candidate">
      <package pattern="Zendiator" />
      <package pattern="Zendiator.*" />
    </packageSource>
    <packageSource key="dependencies">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $Path -Encoding utf8
