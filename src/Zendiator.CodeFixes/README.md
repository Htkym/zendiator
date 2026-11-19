# Zendiator.CodeFixes

When ZEN0035 identifies a handler that is missing its attribute, the **Add Handler attribute** action in Visual Studio adds `[Handler]` to that type. Fix All applies the change within the current document. Review the handler selection and pipeline order in your application.

Install the optional code fix in the project that contains the handler. Use the same version as Zendiator.

```xml
<PackageReference Include="Zendiator.CodeFixes" Version="1.0.0" PrivateAssets="all" />
```

Restore the package and open the suggested fix on the diagnostic in your editor.
