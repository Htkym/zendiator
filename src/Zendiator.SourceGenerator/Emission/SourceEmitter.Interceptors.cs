using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    internal static string EmitInterceptors(GenerationTarget target)
    {
        if (target.CallSites.Count == 0)
            return "";
        var b = CreateSourceBuilder();
        EmitShims(b, target);
        return b.ToString();
    }

    private static void EmitShims(StringBuilder b, GenerationTarget target)
    {
        b.AppendLine("""
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute : System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }
            namespace Zendiator.Generated.Interceptors
            {
                static class AddZendiatorShims
                {
            """);
        var index = 0;
        foreach (var (version, data, isLambda, isExtensionForm) in target.CallSites)
        {
            var name = "Shim" + index++;
            var receiver = isExtensionForm ? "this " : "";
            b.AppendLine($$"""        [System.Runtime.CompilerServices.InterceptsLocation({{version}}, "{{EscapeString(data)}}")]""");
            if (isLambda)
            {
                b.AppendLine($$"""
                            public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {{name}}({{receiver}}global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::System.Action<global::Zendiator.DependencyInjection.ZendiatorConfiguration>? configure)
                            {
                                global::System.ArgumentNullException.ThrowIfNull(configure);
                                var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
                                configure(configuration);
                                return {{target.Prefix}}ZendiatorGeneratedRegistrar.Add(services, configuration.Snapshot());
                            }
                    """);
            }
            else
            {
                b.AppendLine($$"""
                            public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {{name}}({{receiver}}global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                            {
                                var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
                                return {{target.Prefix}}ZendiatorGeneratedRegistrar.Add(services, configuration.Snapshot());
                            }
                    """);
            }
        }

        b.AppendLine("""
                }
            }
            """);
    }
}
