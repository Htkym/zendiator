using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class GeneratorTests
{
    private const string Head = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App; [GenerateZendiator] public sealed partial class Zendiator; ";
    private const string Request = "public readonly record struct Ping : IRequest<int>; ";
    private const string Handler = "public sealed class Handler : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping request, CancellationToken ct) => new(1); } ";
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    private static CSharpCompilation Compilation(string source, string name = "Test") => CSharpCompilation.Create(name,
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ZendiatorGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview), driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    private static GeneratorDriverRunResult Run(CSharpCompilation input, bool success)
    {
        var driver = Driver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        if (success)
        {
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            using var stream = new MemoryStream();
            Assert.True(output.Emit(stream).Success, string.Join("\n", output.GetDiagnostics()));
        }
        return driver.GetRunResult();
    }

    [Fact]
    public void Typed_source_compiles_and_explicit_handlers_work()
    {
        var result = Run(Compilation(Head + Request + Handler.Replace("public ValueTask<int> HandleAsync", "ValueTask<int> IRequestHandler<Ping,int>.HandleAsync")), true);
        var source = result.GeneratedTrees.Single().ToString();
        Assert.Contains("SendAsync(global::App.Ping request", source);
        Assert.DoesNotContain("MakeGeneric", source);
        Assert.DoesNotContain("MessagePipe", source);
        // Only concrete overloads are generated; no generic object/IRequest dispatch.
        Assert.DoesNotContain("SendAsync<T", source);
        Assert.DoesNotContain("object request", source);
        Assert.DoesNotContain("IRequest<", source.Replace("IRequestContinuation<", "").Replace("IRequestHandler<", ""));
        Assert.Contains("AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime", source);
    }

    [Theory]
    [InlineData("ZEN0001", "public readonly record struct Ping : IRequest<int>;")]
    [InlineData("ZEN0002", Request + Handler + "public sealed class Other : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(2); }")]
    [InlineData("ZEN0003", "public sealed class Ping : IRequest<int>, IRequest<string>;")]
    [InlineData("ZEN0003", "public readonly record struct Ping<T> : IRequest<T>, IRequest<int>;")]
    [InlineData("ZEN0003", Request + "public sealed class BadHandler : IRequestHandler<Ping,string> { public ValueTask<string> HandleAsync(Ping r, CancellationToken c) => new(\"\"); }")]
    public void Invalid_contracts_report_stable_diagnostics(string id, string body)
    {
        var result = Run(Compilation(Head + body), false);
        Assert.Contains(result.Diagnostics, d => d.Id == id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Duplicate_pipeline_type_is_diagnosed()
    {
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(Pass), Order = 0), PipelineBehavior(typeof(Pass), Order = 1)]") + Request + Handler + """
            public sealed class Pass : IPipelineBehavior<Ping,int> {
                public ValueTask<int> HandleAsync<N>(Ping r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ping,int> => n.InvokeAsync(r, c);
            }
            """;
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0004");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Duplicate_pipeline_order_is_diagnosed()
    {
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(PassA), Order = 0), PipelineBehavior(typeof(PassB), Order = 0)]") + Request + Handler + """
            public sealed class PassA : IPipelineBehavior<Ping,int> {
                public ValueTask<int> HandleAsync<N>(Ping r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ping,int> => n.InvokeAsync(r, c);
            }
            public sealed class PassB : IPipelineBehavior<Ping,int> {
                public ValueTask<int> HandleAsync<N>(Ping r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ping,int> => n.InvokeAsync(r, c);
            }
            """;
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0004");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Unmatched_closed_behavior_and_open_behavior_misuse_are_diagnosed()
    {
        var noMatch = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(Lonely))]") + Request + Handler + """
            public readonly record struct Ghost : IRequest<string>;
            public sealed class Lonely : IPipelineBehavior<Ghost,string> {
                public ValueTask<string> HandleAsync<N>(Ghost r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ghost,string> => n.InvokeAsync(r, c);
            }
            """;
        Assert.Contains(Run(Compilation(noMatch), false).Diagnostics, d => d.Id == "ZEN0004");

        var badOpen = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(Bad<,>))]") + Request + Handler + """
            public class Bad<T,R> {
                public ValueTask<R> HandleAsync<N>(T r, N n, CancellationToken c) where N : struct, IRequestContinuation<T,R> => n.InvokeAsync(r, c);
            }
            """;
        Assert.Contains(Run(Compilation(badOpen), false).Diagnostics, d => d.Id == "ZEN0004");
    }

    [Fact]
    public void Invalid_pipeline_and_mediator_declarations_are_diagnosed()
    {
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(string))]") + Request + Handler;
        Assert.Contains(Run(Compilation(source), false).Diagnostics, d => d.Id == "ZEN0004");
        foreach (var invalid in new[] { "public sealed partial class Wrong;", "public sealed class Zendiator;", "public sealed partial class Zendiator { private int _value; }" })
            Assert.Contains(Run(Compilation(Head.Replace("public sealed partial class Zendiator;", invalid)), false).Diagnostics, d => d.Id == "ZEN0005");
    }

    [Fact]
    public void Duplicate_mediator_and_reserved_names_are_diagnosed()
    {
        var duplicate = Head + Request + Handler + "[GenerateZendiator] public sealed partial class Zendiator;";
        Assert.Contains(Run(Compilation(duplicate), false).Diagnostics, d => d.Id == "ZEN0005");

        var reserved = Head + Request + Handler + "public interface IZendiator {}";
        Assert.Contains(Run(Compilation(reserved), false).Diagnostics, d => d.Id == "ZEN0005");

        var reservedExtensions = Head + Request + Handler + "public static class ZendiatorServiceCollectionExtensions {}";
        Assert.Contains(Run(Compilation(reservedExtensions), false).Diagnostics, d => d.Id == "ZEN0005");
    }

    [Fact]
    public void Same_name_in_different_namespaces_generates_both_routes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; } "
            + "namespace App.A { public readonly record struct Ping : IRequest<int>; public sealed class Handler : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } } "
            + "namespace App.B { public readonly record struct Ping : IRequest<int>; public sealed class Handler : IRequestHandler<Ping,int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(2); } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.A.Ping", text);
        Assert.Contains("global::App.B.Ping", text);
        Assert.Contains("SendAsync(global::App.A.Ping request", text);
        Assert.Contains("SendAsync(global::App.B.Ping request", text);
    }

    [Fact]
    public void Assembly_mode_generates_mediator_without_a_handwritten_class()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: GenerateZendiator(Namespace = \"App.Generated\")] namespace App { "
            + Request + Handler + " }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("namespace App.Generated;", text);
        Assert.Contains("SendAsync(global::App.Ping request", text);
        Assert.Contains("AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services", text);
    }

    [Fact]
    public void Assembly_mode_defaults_namespace_to_assembly_generated()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: GenerateZendiator] namespace App { "
            + Request + Handler + " }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("namespace Test.Generated;", text);
    }

    [Fact]
    public void Assembly_pipeline_behavior_applies_to_routes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: GenerateZendiator(Namespace = \"App.Generated\")] [assembly: PipelineBehavior(typeof(App.Pass))] namespace App { "
            + Request + Handler
            + """
             public sealed class Pass : IPipelineBehavior<Ping,int> {
                 public ValueTask<int> HandleAsync<N>(Ping r, N n, CancellationToken c) where N : struct, IRequestContinuation<Ping,int> => n.InvokeAsync(r, c);
             }
             }
            """;
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.Pass", text);
    }

    [Fact]
    public void Class_and_assembly_modes_together_are_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: Zendiator.GenerateZendiator] namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + Request + Handler + " }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0006");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Assembly_mode_name_collision_is_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: GenerateZendiator(Namespace = \"App.Generated\")] namespace App { "
            + Request + Handler + " } namespace App.Generated { public sealed class Zendiator {} }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0007");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Assembly_include_without_generate_produces_nothing()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: IncludeAssembly(typeof(App.Marker))] namespace App { public sealed class Marker; } "
            + Request + Handler;
        var result = Run(Compilation(source), true);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Void_request_generates_non_generic_send_without_unit()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class DeleteUserHandler : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::System.Threading.Tasks.ValueTask SendAsync(global::App.DeleteUser request", text);
        Assert.DoesNotContain("Unit", text);
    }

    [Fact]
    public void Legacy_unit_handler_keeps_unit_send_alongside_void_routes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Done : ICommand; "
            + "public sealed class DoneHandler : ICommandHandler<Done> { public ValueTask<Unit> HandleAsync(Done r, CancellationToken c) => new(Unit.Value); } "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class DeleteUserHandler : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("ValueTask<global::Zendiator.Unit> SendAsync(global::App.Done request", text);
        Assert.Contains("global::System.Threading.Tasks.ValueTask SendAsync(global::App.DeleteUser request", text);
    }

    [Fact]
    public void Mixed_void_and_unit_handlers_are_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class NewHandler : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } "
            + "public sealed class OldHandler : IRequestHandler<DeleteUser, Unit> { public ValueTask<Unit> HandleAsync(DeleteUser c, CancellationToken ct) => new(Unit.Value); } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0008");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Duplicate_and_missing_void_handlers_are_rejected()
    {
        var duplicate = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class First : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } "
            + "public sealed class Second : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } }";
        var duplicated = Run(Compilation(duplicate), false);
        Assert.Contains(duplicated.Diagnostics, d => d.Id == "ZEN0002");
        Assert.Empty(duplicated.GeneratedTrees);

        var missing = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; }";
        var missed = Run(Compilation(missing), false);
        Assert.Contains(missed.Diagnostics, d => d.Id == "ZEN0001");
        Assert.Empty(missed.GeneratedTrees);
    }

    [Fact]
    public void Response_behavior_does_not_apply_to_void_routes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator, PipelineBehavior(typeof(Pass))] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class DeleteUserHandler : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } "
            + "public sealed class Pass : IPipelineBehavior<DeleteUser, Unit> { "
            + "public ValueTask<Unit> HandleAsync<N>(DeleteUser r, N n, CancellationToken c) where N : struct, IRequestContinuation<DeleteUser, Unit> => n.InvokeAsync(r, c); } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0004");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Open_void_behavior_applies_only_to_void_routes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator, PipelineBehavior(typeof(Trace<>))] public sealed partial class Zendiator; "
            + "public sealed record DeleteUser(int UserId) : ICommand; "
            + "public sealed class DeleteUserHandler : IRequestHandler<DeleteUser> { public ValueTask HandleAsync(DeleteUser c, CancellationToken ct) => default; } "
            + "public readonly record struct Ping : IRequest<int>; "
            + "public sealed class PingHandler : IRequestHandler<Ping, int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } "
            + """
            public sealed class Trace<T> : IPipelineBehavior<T> where T : IRequest {
                public ValueTask HandleAsync<N>(T r, N n, CancellationToken c) where N : struct, IRequestContinuation<T> => n.InvokeAsync(r, c);
            }
            """
            + " }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.Trace<global::App.DeleteUser>", text);
        Assert.DoesNotContain("global::App.Trace<global::App.Ping", text);
    }

    [Fact]
    public void Ref_struct_void_request_is_redirected_to_sync_dispatch()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly ref struct DeleteSpan : IRequest; "
            + "public sealed class DeleteSpanHandler : IRequestHandler<DeleteSpan> { public ValueTask HandleAsync(DeleteSpan c, CancellationToken ct) => default; } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0012");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Open_request_generates_a_generic_send_without_enumerating_callsites()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record GetById<T>(int Id) : IRequest<T> where T : class; "
            + "public sealed class GetByIdHandler<T> : IRequestHandler<GetById<T>, T> where T : class { "
            + "public ValueTask<T> HandleAsync(GetById<T> r, CancellationToken c) => new(default(T)!); } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("ValueTask<T> SendAsync<T>(global::App.GetById<T> request", text);
        Assert.Contains("where T : class", text);
        Assert.Contains("GetRequiredService<global::App.GetByIdHandler<T>>", text);
        Assert.Contains("typeof(global::App.GetByIdHandler<>)", text);
        Assert.DoesNotContain("GetById<User>", text);
    }

    [Fact]
    public void Open_request_supports_reorder_fixed_and_response_substitution()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Lookup<TKey, TValue>(TKey Key) : IRequest<TValue>; "
            + "public sealed class LookupHandler<T, TKey> : IRequestHandler<Lookup<TKey, T>, T> { "
            + "public ValueTask<T> HandleAsync(Lookup<TKey, T> r, CancellationToken c) => new(default(T)!); } "
            + "public sealed record IntLookup<TKey>(TKey Key) : IRequest<int>; "
            + "public sealed class IntLookupHandler<TKey> : IRequestHandler<IntLookup<TKey>, int> { "
            + "public ValueTask<int> HandleAsync(IntLookup<TKey> r, CancellationToken c) => new(1); } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("ValueTask<TValue> SendAsync<TKey, TValue>(global::App.Lookup<TKey, TValue> request", text);
        Assert.Contains("GetRequiredService<global::App.LookupHandler<TValue, TKey>>", text);
        Assert.Contains("ValueTask<int> SendAsync<TKey>(global::App.IntLookup<TKey> request", text);
    }

    [Fact]
    public void Open_void_request_generates_a_generic_void_send()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record DeleteEntities<T>(int[] Ids) : ICommand where T : class; "
            + "public sealed class DeleteEntitiesHandler<T> : IRequestHandler<DeleteEntities<T>> where T : class { "
            + "public ValueTask HandleAsync(DeleteEntities<T> r, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::System.Threading.Tasks.ValueTask SendAsync<T>(global::App.DeleteEntities<T> request", text);
        Assert.Contains("where T : class", text);
    }

    [Fact]
    public void Open_handler_with_uninferrable_response_or_parameter_is_diagnosed()
    {
        var mismatch = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record GetById<T>(int Id) : IRequest<T> where T : class; "
            + "public sealed class BadHandler<T> : IRequestHandler<GetById<T>, string> where T : class { "
            + "public ValueTask<string> HandleAsync(GetById<T> r, CancellationToken c) => new(\"\"); } }";
        var mismatched = Run(Compilation(mismatch), false);
        Assert.Contains(mismatched.Diagnostics, d => d.Id == "ZEN0003");
        Assert.Empty(mismatched.GeneratedTrees);

        var extra = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record GetById<T>(int Id) : IRequest<T> where T : class; "
            + "public sealed class BadHandler<T, TExtra> : IRequestHandler<GetById<T>, T> where T : class { "
            + "public ValueTask<T> HandleAsync(GetById<T> r, CancellationToken c) => new(default(T)!); } }";
        var extended = Run(Compilation(extra), false);
        Assert.Contains(extended.Diagnostics, d => d.Id == "ZEN0009");
        Assert.Empty(extended.GeneratedTrees);
    }

    [Fact]
    public void Closed_and_open_handlers_overlapping_is_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record User(int Id); "
            + "public sealed record GetById<T>(int Id) : IRequest<T> where T : class; "
            + "public sealed class OpenHandler<T> : IRequestHandler<GetById<T>, T> where T : class { "
            + "public ValueTask<T> HandleAsync(GetById<T> r, CancellationToken c) => new(default(T)!); } "
            + "public sealed class ClosedHandler : IRequestHandler<GetById<User>, User> { "
            + "public ValueTask<User> HandleAsync(GetById<User> r, CancellationToken c) => new(new User(1)); } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0010");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Incompatible_open_constraints_are_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Keyed<T>(int Id) : IRequest<T> where T : struct; "
            + "public sealed class BadHandler<T> : IRequestHandler<Keyed<T>, T> where T : class { "
            + "public ValueTask<T> HandleAsync(Keyed<T> r, CancellationToken c) => new(default(T)!); } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0011");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Open_void_and_response_handlers_conflict()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Work<T>(int Id) : ICommand where T : class; "
            + "public sealed class VoidHandler<T> : IRequestHandler<Work<T>> where T : class { "
            + "public ValueTask HandleAsync(Work<T> r, CancellationToken c) => default; } "
            + "public sealed class UnitHandler<T> : IRequestHandler<Work<T>, Unit> where T : class { "
            + "public ValueTask<Unit> HandleAsync(Work<T> r, CancellationToken c) => new(Unit.Value); } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0008");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Notification_generates_ordered_sequential_publish()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record UserCreated(int UserId) : INotification; "
            + "public sealed class First : INotificationHandler<UserCreated> { public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default; } "
            + "[HandlerOrder(Order = 1)] public sealed class Second : INotificationHandler<UserCreated> { public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default; } "
            + "[HandlerOrder(Order = -1)] public sealed class Zeroth : INotificationHandler<UserCreated> { public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("PublishAsync(global::App.UserCreated notification", text);
        Assert.Contains("Publish(global::App.UserCreated notification", text);
        Assert.True(text.IndexOf("Zeroth", StringComparison.Ordinal) < text.IndexOf("First", StringComparison.Ordinal));
        Assert.True(text.IndexOf("First", StringComparison.Ordinal) < text.IndexOf("Second", StringComparison.Ordinal));
        Assert.Contains("notificationType == typeof(global::App.UserCreated)", text);
        Assert.Contains("throw new global::System.InvalidOperationException", text);
    }

    [Fact]
    public void Known_notification_without_subscribers_completes()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Lonely : INotification; "
            + "public readonly record struct Tick(int N) : INotification; "
            + "public sealed class TickHandler : INotificationHandler<Tick> { public ValueTask HandleAsync(Tick n, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("PublishAsync(global::App.Lonely notification", text);
        Assert.Contains("PublishAsync(global::App.Tick notification", text);
    }

    [Fact]
    public void Open_notification_generates_generic_publish()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Changed<T>(T Value) : INotification; "
            + "public sealed class ChangedHandler<T> : INotificationHandler<Changed<T>> { public ValueTask HandleAsync(Changed<T> n, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("PublishAsync<T>(global::App.Changed<T> notification", text);
        Assert.Contains("Publish<T>(global::App.Changed<T> notification", text);
        Assert.Contains("typeof(global::App.ChangedHandler<>)", text);
    }

    [Fact]
    public void Invalid_notification_contracts_are_diagnosed()
    {
        var badTarget = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed class Bad : INotificationHandler<string> { public ValueTask HandleAsync(string n, CancellationToken c) => default; } }";
        var bad = Run(Compilation(badTarget), false);
        Assert.Contains(bad.Diagnostics, d => d.Id == "ZEN0013");
        Assert.Empty(bad.GeneratedTrees);

        var badDecl = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; [assembly: Notification(typeof(App.Pong))] namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Pong : IRequest<int>; "
            + "public sealed class PongHandler : IRequestHandler<Pong, int> { public ValueTask<int> HandleAsync(Pong r, CancellationToken c) => new(1); } }";
        var declined = Run(Compilation(badDecl), false);
        Assert.Contains(declined.Diagnostics, d => d.Id == "ZEN0013");
        Assert.Empty(declined.GeneratedTrees);

        var refNotif = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly ref struct SpanNote : INotification; "
            + "public sealed class SpanHandler : INotificationHandler<SpanNote> { public ValueTask HandleAsync(SpanNote n, CancellationToken c) => default; } }";
        var spanned = Run(Compilation(refNotif), false);
        Assert.Contains(spanned.Diagnostics, d => d.Id == "ZEN0012");
        Assert.Empty(spanned.GeneratedTrees);
    }

    [Fact]
    public void Duplicate_assembly_inclusion_does_not_duplicate_delivery()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator, IncludeAssembly(typeof(Marker)), IncludeAssembly(typeof(Other))] public sealed partial class Zendiator; "
            + "public sealed class Marker; public sealed class Other; "
            + "public sealed record UserCreated(int UserId) : INotification; "
            + "public sealed class Handler : INotificationHandler<UserCreated> { public ValueTask HandleAsync(UserCreated n, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        var count = text.Split("GetRequiredService<global::App.Handler>", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Overlapping_open_and_closed_subscribers_are_diagnosed()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Changed<T>(T Value) : INotification; "
            + "public sealed class OpenHandler<T> : INotificationHandler<Changed<T>> { public ValueTask HandleAsync(Changed<T> n, CancellationToken c) => default; } "
            + "public sealed class ClosedHandler : INotificationHandler<Changed<string>> { public ValueTask HandleAsync(Changed<string> n, CancellationToken c) => default; } }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0010");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Multi_request_generates_send_all_without_single_send()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Quote(string Vendor); "
            + "public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>; "
            + "public sealed class First : IRequestHandler<GetQuotes, Quote> { public ValueTask<Quote> HandleAsync(GetQuotes r, CancellationToken c) => new(new Quote(\"A\")); } "
            + "public sealed class Second : IRequestHandler<GetQuotes, Quote> { public ValueTask<Quote> HandleAsync(GetQuotes r, CancellationToken c) => new(new Quote(\"B\")); } "
            + "public sealed record Broadcast(string Message) : IMultiRequest; "
            + "public sealed class Cast : IRequestHandler<Broadcast> { public ValueTask HandleAsync(Broadcast r, CancellationToken c) => default; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("IReadOnlyList<global::App.Quote>> SendAllAsync(global::App.GetQuotes request", text);
        Assert.Contains("global::System.Threading.Tasks.ValueTask SendAllAsync(global::App.Broadcast request", text);
        Assert.DoesNotContain("SendAsync(global::App.GetQuotes", text);
        Assert.DoesNotContain("SendAsync(global::App.Broadcast", text);
        Assert.DoesNotContain("Unit", text);
    }

    [Fact]
    public void Multi_request_without_handlers_and_conflicts_are_diagnosed()
    {
        var missing = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Quote(string Vendor); "
            + "public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>; }";
        var missed = Run(Compilation(missing), false);
        Assert.Contains(missed.Diagnostics, d => d.Id == "ZEN0001");
        Assert.Empty(missed.GeneratedTrees);

        var both = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Both : IMultiRequest<Unit>, IMultiRequest; "
            + "public sealed class Handler : IRequestHandler<Both, Unit> { public ValueTask<Unit> HandleAsync(Both r, CancellationToken c) => new(Unit.Value); } }";
        var conflicted = Run(Compilation(both), false);
        Assert.Contains(conflicted.Diagnostics, d => d.Id == "ZEN0008");
        Assert.Empty(conflicted.GeneratedTrees);

        var legacy = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Broadcast(string Message) : IMultiRequest; "
            + "public sealed class Old : IRequestHandler<Broadcast, Unit> { public ValueTask<Unit> HandleAsync(Broadcast r, CancellationToken c) => new(Unit.Value); } "
            + "public sealed class Fresh : IRequestHandler<Broadcast> { public ValueTask HandleAsync(Broadcast r, CancellationToken c) => default; } }";
        var legacied = Run(Compilation(legacy), false);
        Assert.Contains(legacied.Diagnostics, d => d.Id == "ZEN0008");
        Assert.Empty(legacied.GeneratedTrees);
    }

    [Fact]
    public void Open_multi_generates_generic_send_all()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record FanOut<T>(int Id) : IMultiRequest<string>; "
            + "public sealed class First<T> : IRequestHandler<FanOut<T>, string> { public ValueTask<string> HandleAsync(FanOut<T> r, CancellationToken c) => new(\"a\"); } "
            + "public sealed class Second<T> : IRequestHandler<FanOut<T>, string> { public ValueTask<string> HandleAsync(FanOut<T> r, CancellationToken c) => new(\"b\"); } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("IReadOnlyList<string>> SendAllAsync<T>(global::App.FanOut<T> request", text);
        Assert.Contains("typeof(global::App.First<>)", text);
        Assert.Contains("typeof(global::App.Second<>)", text);
    }

    [Fact]
    public void Sync_ref_request_generates_scoped_send_sync()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly ref struct ParseRequest : ISyncRequest<int> { public ParseRequest(ReadOnlySpan<byte> data) => Data = data; public ReadOnlySpan<byte> Data { get; } } "
            + "public sealed class ParseHandler : ISyncRequestHandler<ParseRequest, int> { public int Handle(scoped ParseRequest r, CancellationToken c) => r.Data.Length; } "
            + "public readonly record struct Flush(int Id) : ISyncRequest; "
            + "public sealed class FlushHandler : ISyncRequestHandler<Flush> { public void Handle(Flush r, CancellationToken c) { } } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("int SendSync(scoped global::App.ParseRequest request", text);
        Assert.Contains("void SendSync(global::App.Flush request", text);
        Assert.DoesNotContain("SendAsync(global::App.ParseRequest", text);
        Assert.DoesNotContain("SendAsync(global::App.Flush", text);
        Assert.DoesNotContain("object request", text);
        Assert.Contains("SyncRoute0Node0(global::System.IServiceProvider services)", text);
        Assert.DoesNotContain("static global::App.ParseRequest", text);
    }

    [Fact]
    public void Sync_behaviors_apply_per_route_without_async_crosstalk()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator, PipelineBehavior(typeof(Trace<,>)), PipelineBehavior(typeof(Async))] public sealed partial class Zendiator; "
            + "public readonly record struct Work(int Value) : ISyncRequest<int>; "
            + "public sealed class WorkHandler : ISyncRequestHandler<Work, int> { public int Handle(Work r, CancellationToken c) => r.Value; } "
            + """
            public sealed class Trace<T, R> : ISyncPipelineBehavior<T, R> where T : ISyncRequest<R>, allows ref struct {
                public R Handle<N>(scoped T r, N n, CancellationToken c) where N : struct, ISyncRequestContinuation<T, R> => n.Invoke(r, c);
            }
            public sealed class Async : IPipelineBehavior<Work, int> {
                public ValueTask<int> HandleAsync<N>(Work r, N n, CancellationToken c) where N : struct, IRequestContinuation<Work, int> => n.InvokeAsync(r, c);
            }
            """
            + " }";
        var result = Run(Compilation(source), false);
        Assert.Contains(result.Diagnostics, d => d.Id == "ZEN0004");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Sync_multi_generates_send_all_sync()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Sum(int A, int B) : ISyncMultiRequest<int>; "
            + "public sealed class SumA : ISyncRequestHandler<Sum, int> { public int Handle(Sum r, CancellationToken c) => r.A + r.B; } "
            + "public sealed class SumB : ISyncRequestHandler<Sum, int> { public int Handle(Sum r, CancellationToken c) => r.A * r.B; } "
            + "public readonly record struct Reset(string Name) : ISyncMultiRequest; "
            + "public sealed class ResetHandler : ISyncRequestHandler<Reset> { public void Handle(Reset r, CancellationToken c) { } } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("IReadOnlyList<int> SendAllSync(global::App.Sum request", text);
        Assert.Contains("void SendAllSync(global::App.Reset request", text);
        Assert.DoesNotContain("Unit", text);
    }

    [Fact]
    public void Open_sync_generic_ref_generates_allows_send_sync()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly ref struct Box<T> : ISyncRequest<int> where T : allows ref struct { public readonly T Value; public Box(T v) => Value = v; } "
            + "public sealed class BoxHandler<T> : ISyncRequestHandler<Box<T>, int> where T : allows ref struct { public int Handle(scoped Box<T> r, CancellationToken c) => 1; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("int SendSync<T>(scoped global::App.Box<T> request", text);
        Assert.Contains("where T : allows ref struct", text);
        Assert.Contains("GetRequiredService<global::App.BoxHandler<T>>", text);
    }

    [Fact]
    public void Explicit_sync_handler_keeps_a_typed_cast()
    {
        var source = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Work(int Value) : ISyncRequest<int>; "
            + "public sealed class WorkHandler : ISyncRequestHandler<Work, int> { int ISyncRequestHandler<Work, int>.Handle(Work r, CancellationToken c) => r.Value; } }";
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("((global::Zendiator.ISyncRequestHandler<global::App.Work, int>)", text);
    }

    [Fact]
    public void Sync_async_confusion_and_ref_misuse_are_diagnosed()
    {
        var confused = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public sealed record Ping : IRequest<int>; "
            + "public sealed class PingHandler : IRequestHandler<Ping, int> { public ValueTask<int> HandleAsync(Ping r, CancellationToken c) => new(1); } "
            + "public sealed class SyncPingHandler : ISyncRequestHandler<Ping, int> { public int Handle(Ping r, CancellationToken c) => 1; } }";
        var mixed = Run(Compilation(confused), false);
        Assert.Contains(mixed.Diagnostics, d => d.Id == "ZEN0008");
        Assert.Empty(mixed.GeneratedTrees);

        var asyncRef = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly ref struct SpanNote : IRequest<int>; "
            + "public sealed class SpanHandler : IRequestHandler<SpanNote, int> { public ValueTask<int> HandleAsync(SpanNote r, CancellationToken c) => new(1); } }";
        var boxed = Run(Compilation(asyncRef), false);
        Assert.Contains(boxed.Diagnostics, d => d.Id == "ZEN0012");
        Assert.Empty(boxed.GeneratedTrees);

        var refResponse = "using Zendiator; using System; using System.Threading; using System.Threading.Tasks; namespace App { [GenerateZendiator] public sealed partial class Zendiator; "
            + "public readonly record struct Work(int Value) : ISyncRequest<ReadOnlySpan<byte>>; "
            + "public sealed class WorkHandler : ISyncRequestHandler<Work, ReadOnlySpan<byte>> { public ReadOnlySpan<byte> Handle(Work r, CancellationToken c) => default; } }";
        var refed = Run(Compilation(refResponse), false);
        Assert.Contains(refed.Diagnostics, d => d.Id == "ZEN0012");
        Assert.Empty(refed.GeneratedTrees);
    }

    [Fact]
    public void Output_is_deterministic_across_runs()
    {
        var compilation = Compilation(Head + Request + Handler);
        var first = Driver().RunGenerators(compilation).GetRunResult().GeneratedTrees.Single().ToString();
        var second = Driver().RunGenerators(compilation).GetRunResult().GeneratedTrees.Single().ToString();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Zero_behaviors_generate_direct_handler_nodes()
    {
        var text = Run(Compilation(Head + Request + Handler), true).GeneratedTrees.Single().ToString();
        Assert.DoesNotContain("Route0Node", text);
        Assert.DoesNotContain("IRequestContinuation", text);
        Assert.Contains("GetRequiredService<global::App.Handler>", text);
    }

    [Fact]
    public void Derived_command_and_query_contracts_share_one_handler_slot()
    {
        var source = Head + """
            public readonly record struct DoWork(int Value) : ICommand<int>;
            public sealed class DoWorkHandler : ICommandHandler<DoWork,int> {
                public ValueTask<int> HandleAsync(DoWork r, CancellationToken c) => new(r.Value);
            }
            public readonly record struct Fetch(int Value) : IQuery<int>;
            public sealed class FetchHandler : IQueryHandler<Fetch,int> {
                public ValueTask<int> HandleAsync(Fetch r, CancellationToken c) => new(r.Value);
            }
            public readonly record struct Done : ICommand;
            public sealed class DoneHandler : ICommandHandler<Done> {
                public ValueTask<Unit> HandleAsync(Done r, CancellationToken c) => new(Unit.Value);
            }
            """;
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.DoWork", text);
        Assert.Contains("global::App.Fetch", text);
        Assert.Contains("global::App.Done", text);
    }

    [Fact]
    public void Handler_in_referenced_assembly_is_discovered_with_exact_request()
    {
        var contracts = Compilation("""
            using Zendiator;
            namespace Contracts;
            public readonly record struct Remote : IRequest<int>;
            public sealed class RemoteHandler : IRequestHandler<Remote,int> {
                public System.Threading.Tasks.ValueTask<int> HandleAsync(Remote r, System.Threading.CancellationToken c) => new(7);
            }
            public sealed class Marker;
            """, "Contracts");
        using var stream = new MemoryStream();
        Assert.True(contracts.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, IncludeAssembly(typeof(Contracts.Marker))]");
        var text = Run(Compilation(source).AddReferences(reference), true).GeneratedTrees.Single().ToString();
        Assert.Contains("Contracts.Remote", text);
        Assert.Contains("Contracts.RemoteHandler", text);
    }

    [Fact]
    public void New_and_nested_generic_constraints_are_respected()
    {
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(NewOnly<,>), Order = 0), PipelineBehavior(typeof(Compare<,>), Order = 1)]")
            + Request + Handler + """
            public class NewOnly<T,R> : IPipelineBehavior<T,R> where T : IRequest<R>, new() where R : new() {
                public ValueTask<R> HandleAsync<N>(T r, N next, CancellationToken ct)
                  where N : struct, IRequestContinuation<T,R> => next.InvokeAsync(r,ct);
            }
            public class Compare<T,R> : IPipelineBehavior<T,R> where T : IRequest<R>, System.IComparable<System.Collections.Generic.List<R>> {
                public ValueTask<R> HandleAsync<N>(T r, N next, CancellationToken ct)
                  where N : struct, IRequestContinuation<T,R> => next.InvokeAsync(r,ct);
            }
            public readonly record struct Fresh : IRequest<System.Guid>;
            public sealed class FreshHandler : IRequestHandler<Fresh,System.Guid> {
                public ValueTask<System.Guid> HandleAsync(Fresh r, CancellationToken c) => new(System.Guid.NewGuid());
            }
            """;
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.DoesNotContain("MessagePipe", text);
        Assert.Contains("global::App.Ping", text);
        Assert.Contains("global::App.Fresh", text);
        Assert.Contains("global::App.NewOnly<global::App.Ping,", text);
        Assert.DoesNotContain("global::App.Compare<global::App.Ping,", text);
    }

    [Fact]
    public void Referenced_assembly_and_nested_request_compile()
    {
        var contracts = Compilation("using Zendiator; namespace Contracts; public class Marker { public readonly record struct Ping : IRequest<int>; }", "Contracts");
        using var stream = new MemoryStream();
        Assert.True(contracts.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, IncludeAssembly(typeof(Contracts.Marker))]") +
            Handler.Replace("Ping", "Contracts.Marker.Ping");
        Run(Compilation(source).AddReferences(reference), true);
    }

    [Fact]
    public void Constrained_generic_behavior_applies_only_to_matching_requests()
    {
        var source = Head.Replace("[GenerateZendiator]", "[GenerateZendiator, PipelineBehavior(typeof(Pass<,>))]") + Request + Handler + """
            public class Pass<T,R> : IPipelineBehavior<T,R> where T : struct, IRequest<R> {
                public ValueTask<R> HandleAsync<N>(T r, N next, CancellationToken ct)
                  where N : struct, IRequestContinuation<T,R> => next.InvokeAsync(r,ct);
            }
            public sealed record RefPing : IRequest<int>;
            public sealed class RefHandler : IRequestHandler<RefPing,int> {
                public ValueTask<int> HandleAsync(RefPing r,CancellationToken c) => new(0);
            }
            """;
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.Pass<global::App.Ping,", text);
        Assert.DoesNotContain("global::App.Pass<global::App.RefPing", text);
    }

    [Fact]
    public void Changed_requests_do_not_reuse_previous_analysis_state()
    {
        var driver = Driver().RunGeneratorsAndUpdateCompilation(Compilation(Head + Request + Handler), out _, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Contains("global::App.Ping request", driver.GetRunResult().GeneratedTrees.Single().ToString());

        var changedRequest = Request.Replace("Ping", "Changed");
        driver = driver.RunGeneratorsAndUpdateCompilation(Compilation(Head + changedRequest), out _, out diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "ZEN0001");
        Assert.Empty(driver.GetRunResult().GeneratedTrees);

        driver = driver.RunGeneratorsAndUpdateCompilation(Compilation(Head + changedRequest + Handler.Replace("Ping", "Changed")), out var output, out diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.Contains("global::App.Changed request", source);
        Assert.DoesNotContain("global::App.Ping", source);
    }

    [Fact]
    public void Unrelated_edits_reuse_source_output()
    {
        var compilation = Compilation(Head + Request + Handler);
        var driver = Driver().RunGenerators(compilation);
        var first = driver.GetRunResult().GeneratedTrees.Single().ToString();
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other { class Unrelated {} }", new CSharpParseOptions(LanguageVersion.Preview))));
        Assert.Equal(first, driver.GetRunResult().GeneratedTrees.Single().ToString());
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["SourceText"].SelectMany(s => s.Outputs),
            output => Assert.Contains(output.Reason, new[] { IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Cached }));
    }

    [Fact]
    public void Implicit_handlers_use_direct_calls_explicit_ones_keep_casts()
    {
        var source = Head + Request + Handler + """
            public readonly record struct Pong : IRequest<int>;
            public sealed class PongHandler : IRequestHandler<Pong,int> {
                ValueTask<int> IRequestHandler<Pong,int>.HandleAsync(Pong r, CancellationToken c) => new(1);
            }
            """;
        var text = Run(Compilation(source), true).GeneratedTrees.Single().ToString();
        Assert.Contains("_services.GetRequiredService<global::App.Handler>().HandleAsync(request, cancellationToken)", text);
        Assert.Contains("((global::Zendiator.IRequestHandler<global::App.Pong, int>)", text);
    }
}
