using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Competitive;

internal static class RegistrationAudit
{
    public static void Record(IServiceCollection services, string library, string lifetime, string stage)
    {
        if (Environment.GetEnvironmentVariable("COLD_AUDIT_REG") != "1") return;
        var directory = Environment.GetEnvironmentVariable("COLD_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("COLD_EVIDENCE_DIR is required for registration audit.");
        Directory.CreateDirectory(directory);
        var descriptors = services.Select((descriptor, index) => new
        {
            index,
            serviceType = descriptor.ServiceType.FullName,
            assembly = descriptor.ServiceType.Assembly.GetName().Name,
            lifetime = descriptor.Lifetime.ToString(),
            keyed = descriptor.IsKeyedService,
            key = descriptor.IsKeyedService ? descriptor.ServiceKey?.ToString() : null,
            implementationType = (descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType)?.FullName,
            implementationFactory = descriptor.IsKeyedService
                ? descriptor.KeyedImplementationFactory?.Method.ToString()
                : descriptor.ImplementationFactory?.Method.ToString(),
            implementationInstance = (descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance)?.GetType().FullName
        });
        var file = Path.Combine(directory, $"descriptors-{library}-{lifetime}-{stage}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(descriptors, new JsonSerializerOptions { WriteIndented = true }));
    }
}
