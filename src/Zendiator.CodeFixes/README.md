# Zendiator.CodeFixes

Optional C# code fixes for the Zendiator 0.5.0 development API. The runtime and
source generator remain in the `Zendiator` package; this package is installed
separately and is not a runtime dependency.

Use the same candidate version from your local package feed:

```xml
<PackageReference Include="Zendiator.CodeFixes"
                  Version="$(ZendiatorPackageVersion)"
                  PrivateAssets="all" />
```

Set `ZendiatorPackageVersion` to the candidate version you are evaluating.
This development package has not been published to NuGet.org.

The fix adds `[ZendiatorHandler]` when a ZEN0035 diagnostic identifies a missing
handler marker. Fix All is limited to the current document. It does not rewrite
Behavior order, continuations, scopes, or other migration semantics.

The assembly is supplied as a C# analyzer asset and is built against Roslyn C#
Workspaces 4.14.0. Roslyn and Workspaces assemblies are supplied by the development
host, not bundled into this package. The command-line compiler does not apply code
fixes.

One missing-marker example has been checked in Visual Studio 2026 Insiders 18.11,
including suggestion display, fix application, and a successful build. The minimum
supported IDE version and compatibility with other IDEs remain unverified.
