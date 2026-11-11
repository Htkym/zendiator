using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData("Handler")]
    [InlineData("HandlerAttribute")]
    [InlineData("global::Zendiator.Handler")]
    [InlineData("global::Zendiator.HandlerAttribute")]
    public void Handler_marker_forms_compile_with_handler_named_Handler(string marker)
    {
        var source = Head.Replace("[GenerateZendiator] public sealed partial class Zendiator;", "") + Request + Handler.Replace("public sealed class Handler", $"[{marker}] public sealed class Handler");
        var result = Run(Compilation(source), true);
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("typeof(global::App.Handler)"));
    }

    [Fact]
    public void Handler_marker_qualified_name_ignores_a_foreign_same_name_attribute()
    {
        var source = Head.Replace("[GenerateZendiator] public sealed partial class Zendiator;", "") + "public sealed class HandlerAttribute : System.Attribute {}" + Request
            + Handler.Replace("public sealed class Handler", "[global::Zendiator.HandlerAttribute] public sealed class Handler");
        var result = Run(Compilation(source), true);
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("typeof(global::App.Handler)"));
    }
}
