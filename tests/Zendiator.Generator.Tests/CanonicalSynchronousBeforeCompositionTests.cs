using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public async Task Plain_SendAsync_closed_generic_before_routes_preserve_typed_response_and_composed_dispatch()
    {
        const string source = """
            using System;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;using Zendiator;using Zendiator.DependencyInjection;
            namespace BeforeComposition {
                public sealed class Payload;
                public sealed class Tag;
                public sealed record Query<L,R>(L Value,int Step=0):IRequest<L> where L:class,new() where R:class,new();
                public sealed record Command<L,R>(L Value,int Step=0):IRequest where L:class,new() where R:class,new();
                public sealed class Probe {
                    public CancellationToken Token;public object Value=null!;public readonly List<string> Trace=new();
                    public void Hit(string role,object value,int step,CancellationToken token){
                        if(token!=Token||!ReferenceEquals(value,Value)||step!=(role.EndsWith("before",StringComparison.Ordinal)?0:1))
                            throw new InvalidOperationException("Generic route request/token mismatch");
                        Trace.Add(role);
                    }
                }
                public sealed class QueryBefore<X,Y>:ISendRequestTransform<Query<Y,X>> where X:class,new() where Y:class,new(){
                    private readonly Probe probe;public QueryBefore(Probe probe){this.probe=probe;}
                    public Query<Y,X> Before(Query<Y,X> request,CancellationToken token){
                        probe.Hit("query.before",request.Value,request.Step,token);return request with{Step=request.Step+1};
                    }
                }
                [ZendiatorHandler]public sealed class QueryHandler<X,Y>:IRequestHandler<Query<Y,X>,Y> where X:class,new() where Y:class,new(){
                    private readonly Probe probe;public QueryHandler(Probe probe){this.probe=probe;}
                    public ValueTask<Y> HandleAsync(Query<Y,X> request,CancellationToken token){
                        probe.Hit("query.handler",request.Value,request.Step,token);return new(request.Value);
                    }
                }
                public sealed class CommandBefore<X,Y>:ISendRequestTransform<Command<Y,X>> where X:class,new() where Y:class,new(){
                    private readonly Probe probe;public CommandBefore(Probe probe){this.probe=probe;}
                    public Command<Y,X> Before(Command<Y,X> request,CancellationToken token){
                        probe.Hit("command.before",request.Value,request.Step,token);return request with{Step=request.Step+1};
                    }
                }
                [ZendiatorHandler]public sealed class CommandHandler<X,Y>:IRequestHandler<Command<Y,X>> where X:class,new() where Y:class,new(){
                    private readonly Probe probe;public CommandHandler(Probe probe){this.probe=probe;}
                    public ValueTask HandleAsync(Command<Y,X> request,CancellationToken token){
                        probe.Hit("command.handler",request.Value,request.Step,token);return default;
                    }
                }
                [ZendiatorComposition("closed-generic-before-composition")]public sealed class Composition;
                public static class Entry {
                    public static async Task<string[]> Run(){
                        var probe=new Probe{Value=new Payload()};IServiceCollection services=new ServiceCollection();services.AddSingleton(probe);
                        services.AddZendiator<Composition>(c=>{
                            c.Handlers<Query<Payload,Tag>>(DispatchFamily.SendAsync,typeof(QueryHandler<Tag,Payload>));
                            c.Pipeline<Query<Payload,Tag>>(DispatchFamily.SendAsync,Stage.Hook<QueryBefore<Tag,Payload>>());
                            c.Handlers<Command<Payload,Tag>>(DispatchFamily.SendAsync,typeof(CommandHandler<Tag,Payload>));
                            c.Pipeline<Command<Payload,Tag>>(DispatchFamily.SendAsync,Stage.Hook<CommandBefore<Tag,Payload>>());
                        });
                        using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                        using var scope=provider.CreateScope();var mediator=scope.ServiceProvider.GetRequiredService<Generated.Composition.IZendiator>();
                        using var cancellation=new CancellationTokenSource();
                        foreach(var token in new[]{CancellationToken.None,cancellation.Token}){
                            probe.Token=token;var value=(Payload)probe.Value;
                            if(!ReferenceEquals(value,await mediator.SendAsync(new Query<Payload,Tag>(value),token)))
                                throw new InvalidOperationException("Generic response identity mismatch");
                            await mediator.SendAsync(new Command<Payload,Tag>(value),token);
                        }
                        return probe.Trace.ToArray();
                    }
                }
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(
            SyncCompilation(source, "ClosedGenericBeforeComposition_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var path = Path.Combine(evidence, "sync-before-closed-generic-composition");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "input.cs"), source);
            foreach (var generated in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(path, generated.HintName), generated.SourceText.ToString());
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var trace = await assembly.GetType("BeforeComposition.Entry")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string[]>>>()().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "query.before", "query.handler", "command.before", "command.handler",
            "query.before", "query.handler", "command.before", "command.handler" }, trace);
    }
}
