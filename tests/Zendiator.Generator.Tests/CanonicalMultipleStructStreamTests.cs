using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string MultipleStructStreamSource()
    {
        var first = ClosedModule(PublicValueRequestSource(false), "FirstValue", "SharedState", "Items", "H", "F1", "M", "F2", "Source");
        var second = ClosedModule(PublicValueRequestSource(false), "SecondValue", "SharedState", "Items", "H", "F1", "M", "F2", "Source");
        return first + "\n" + second + """
            namespace MultipleValues {
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Zendiator.Generator.Tests;
            using A=global::Closed.FirstValue;using B=global::Closed.SecondValue;
            [ZendiatorComposition("multiple-struct-streams")]public sealed class Many;
            public static class Bridge {
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{
                    c.Handlers<A.Items>(DispatchFamily.Stream,typeof(A.H));
                    c.Handlers<B.Items>(DispatchFamily.Stream,typeof(B.H));
                    c.Pipeline<A.Items>(DispatchFamily.Stream,Stage.Hook<A.F1>(),Stage.Hook<A.M>(),Stage.Hook<A.F2>());
                    c.Pipeline<B.Items>(DispatchFamily.Stream,Stage.Hook<B.F1>(),Stage.Hook<B.M>(),Stage.Hook<B.F2>());
                });
                private static void Check(bool value,string message)=>PublicValueRequestProbe.Check(value,message);
                private static void CheckRoute(PublicValueRequestProbe p,int marker,long pad,object shared,int version){
                    Check(p.Starts==1&&p.SourceDisposals==1,"one source lifecycle per route");
                    Check(p.Seen.Count==10&&p.Seen.All(s=>s.Marker==marker&&s.Pad==pad&&ReferenceEquals(s.Shared,shared)),"route-specific typed request and reference state");
                    Check(p.Seen[0].Version==version&&p.Seen.Skip(1).All(s=>s.Version==version+1),"route-specific shared version");
                    Check(p.Resolved.SequenceEqual(new[]{"H","F1","M","F2"}),"one borrowed component set per route");
                    Check(p.Ended.Count==0,"borrowed components remain until scope disposal");
                }
                private static object Snapshot(PublicValueRequestProbe p,object shared)=>new{
                    p.Starts,p.SourceDisposals,p.Resolved,p.Ended,
                    seen=p.Seen.Select(s=>new{s.Phase,s.Marker,s.Pad,s.Version,shared=ReferenceEquals(s.Shared,shared)}).ToArray()
                };
                public static async Task<string> Run(){
                    var first=new PublicValueRequestProbe();var second=new PublicValueRequestProbe();
                    IServiceCollection services=new ServiceCollection();
                    services.AddScoped<A.H>(_=>new A.H(first));services.AddScoped<A.F1>(_=>new A.F1(first));
                    services.AddScoped<A.M>(_=>new A.M(first));services.AddScoped<A.F2>(_=>new A.F2(first));
                    services.AddScoped<B.H>(_=>new B.H(second));services.AddScoped<B.F1>(_=>new B.F1(second));
                    services.AddScoped<B.M>(_=>new B.M(second));services.AddScoped<B.F2>(_=>new B.F2(second));
                    Register(services);Check(services.GroupBy(d=>d.ServiceType).All(g=>g.Count()==1),"unique DI descriptors");
                    using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                    var scope=provider.CreateScope();
                    var mediator=scope.ServiceProvider.GetRequiredService<global::MultipleValues.Generated.Many.IZendiator>();
                    Check(ReferenceEquals(mediator,scope.ServiceProvider.GetRequiredService<global::MultipleValues.Generated.Many.IZendiator>()),"shared composition identity");
                    var firstShared=new A.SharedState{Version=11};var secondShared=new B.SharedState{Version=23};
                    var firstRequest=new A.Items(firstShared){Marker=17,Pad=101};var secondRequest=new B.Items(secondShared){Marker=29,Pad=202};
                    var firstStream=mediator.StreamAsync(firstRequest);var secondStream=mediator.StreamAsync(secondRequest);
                    var firstCursor=firstStream.GetAsyncEnumerator();var secondCursor=secondStream.GetAsyncEnumerator();
                    Check(first.Starts==0&&second.Starts==0&&first.Resolved.Count==0&&second.Resolved.Count==0,"both routes lazy before first move");
                    var firstValues=new List<int>();var secondValues=new List<int>();var interleaved=new List<string>();
                    try{
                        Check(await firstCursor.MoveNextAsync(),"first route item1");firstValues.Add(firstCursor.Current);interleaved.Add("first:1");
                        Check(first.Starts==1&&second.Starts==0&&second.Resolved.Count==0&&secondShared.Version==23,"starting first leaves second untouched");
                        Check(await secondCursor.MoveNextAsync(),"second route item1");secondValues.Add(secondCursor.Current);interleaved.Add("second:1");
                        Check(await firstCursor.MoveNextAsync(),"first route item2");firstValues.Add(firstCursor.Current);interleaved.Add("first:2");
                        Check(await secondCursor.MoveNextAsync(),"second route item2");secondValues.Add(secondCursor.Current);interleaved.Add("second:2");
                        Check(!await firstCursor.MoveNextAsync(),"first route end");interleaved.Add("first:end");
                        Check(!await secondCursor.MoveNextAsync(),"second route end");interleaved.Add("second:end");
                    }finally{await secondCursor.DisposeAsync();await firstCursor.DisposeAsync();}
                    Check(firstValues.SequenceEqual(new[]{2,4})&&secondValues.SequenceEqual(new[]{2,4}),"independent results");
                    CheckRoute(first,17,101,firstShared,11);CheckRoute(second,29,202,secondShared,23);
                    Check(firstShared.Version==12&&secondShared.Version==24,"independent shared mutation");
                    scope.Dispose();
                    Check(first.Ended.SequenceEqual(new[]{"F2","M","F1","H"})&&second.Ended.SequenceEqual(new[]{"F2","M","F1","H"}),"scope owns each borrowed route");
                    return JsonSerializer.Serialize(new{generatorInputs=1,runtimeEnumerations=2,firstValues,secondValues,interleaved,
                        first=Snapshot(first,firstShared),second=Snapshot(second,secondShared)});
                }
            }}
            """;
    }

    [Fact]
    public async Task Multiple_struct_Stream_routes_preserve_state_identity_and_borrowed_ownership_when_interleaved()
    {
        var source = MultipleStructStreamSource();
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "multiple-struct-streams");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.cs"), source);
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "MultipleStructStreams_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        if (folder != null)
        {
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), JsonSerializer.Serialize(new
            {
                sources = files.Select(f => f.HintName),
                diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage(), span = d.Location.SourceSpan.ToString() }),
                compiler = output.GetDiagnostics().Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var firstRequest = output.GetTypeByMetadataName("Closed.FirstValue.Items");
        var secondRequest = output.GetTypeByMetadataName("Closed.SecondValue.Items");
        Assert.NotNull(firstRequest); Assert.NotNull(secondRequest);
        Assert.True(firstRequest.IsValueType); Assert.True(secondRequest.IsValueType);
        Assert.False(SymbolEqualityComparer.Default.Equals(firstRequest, secondRequest));
        var contract = output.GetTypeByMetadataName("MultipleValues.Generated.Many.IZendiator");
        Assert.NotNull(contract);
        var overloads = contract.GetMembers("StreamAsync").OfType<IMethodSymbol>().ToArray();
        Assert.Equal(2, overloads.Length);
        Assert.Contains(overloads, m => SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, firstRequest));
        Assert.Contains(overloads, m => SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, secondRequest));
        Assert.Equal(2, files.Count(f => IsCanonicalRoute(f)));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var run = assembly.GetType("MultipleValues.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        var result = await run().WaitAsync(TimeSpan.FromSeconds(15));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var observed = JsonDocument.Parse(result); var value = observed.RootElement;
        Assert.Equal(2, value.GetProperty("runtimeEnumerations").GetInt32());
        Assert.Equal(new[] { "first:1", "second:1", "first:2", "second:2", "first:end", "second:end" }, value.GetProperty("interleaved").EnumerateArray().Select(x => x.GetString()));
        foreach (var name in new[] { "first", "second" })
        {
            var route = value.GetProperty(name);
            Assert.Equal(1, route.GetProperty("Starts").GetInt32()); Assert.Equal(1, route.GetProperty("SourceDisposals").GetInt32());
            Assert.Equal(new[] { "F2", "M", "F1", "H" }, route.GetProperty("Ended").EnumerateArray().Select(x => x.GetString()));
        }
    }
}
