namespace Zendiator.SourceGenerator;

// Keep the DI owner's original box for mutable value-type receivers and factories.
// A generic lookup would unbox a copy and each interface conversion would box it again.
internal static class CanonicalServiceResolution
{
    internal static string Emit(string type, bool isValueType) => isValueType
        ? "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(_provider, typeof(" + type + "))"
        : "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<" + type + ">(_provider)";
}
