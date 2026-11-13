using System.Reflection;
using BenchmarkDotNet.Attributes;
using Competitive;

namespace Competitive.FoundatioRelease;

// Observation only, after the existing concrete method assertions have completed.
public static class FoundatioSemanticSnapshot
{
    public static Dictionary<string, object?> Parameters(FoundatioState state) => state.GetType().GetProperties()
        .Where(property => property.IsDefined(typeof(ParamsAttribute)) || property.IsDefined(typeof(ParamsSourceAttribute)))
        .ToDictionary(property => property.Name, property => property.GetValue(state));

    public static object Capture(FoundatioState state, string method, string scenario, Probe probe,
        object? result = null, int operations = 1, object? cancellation = null) => new
        {
            actualType = state.GetType().FullName,
            method = state.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)?.Name
            ?? throw new InvalidOperationException("Semantic observation refers to a missing concrete method"),
            parameters = Parameters(state),
            scenario,
            result,
            operations,
            cancellation,
            events = probe.Events.ToArray(),
            handlers = probe.Handlers,
            items = probe.Items,
            disposals = probe.Disposals,
            registration = state.GetType().GetProperty("RegistrationDescriptors")?.GetValue(state)
        };
}
