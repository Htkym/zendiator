using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class AllWrapperProbe
{
    public readonly NestedWrapperProbe Core = new();
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly List<string> Contexts = [];
    public readonly List<IAsyncOnceGate> BodyGates = [];
    public readonly Exception Failure = new FormatException("first next");
    public string Profile = "", Case = "";
    public int Branch, FailedBodies;
    public string? PendingPoint;
    public string? Skip => Case == "skip" && Branch == 2 ? Profile + ".w0" : null;
    public string? Repeat => null;
    public string? NullNext => null;
    public string? CancelNext => Case == "cancel" && Branch == 2 ? Profile + ".w0" : null;
    public CancellationTokenSource Foreign => Core.Foreign;
    public CancellationTokenSource Token => Core.Token;
    public bool Retry => Case == "retry" && Branch == 2;
    private void Context(string phase)
    {
        Contexts.Add(phase + "|" + Ambient.Value + "|" + (SynchronizationContext.Current?.GetType().FullName ?? "none"));
        Ambient.Value = phase;
    }
    public void Hit(string phase, object request, CancellationToken token, Exception? error = null, SendExitInfo? exit = null)
    {
        if (phase == Profile + ".k0.before") Branch++;
        Context(phase); Core.Hit(phase, request, token, error, exit);
        if (Case == "propagate" && phase == Profile + ".h1.body") throw Failure;
    }
    public bool FailBody(string id) => Retry && id == Profile + ".h1" && FailedBodies++ == 0;
    public ValueTask<int> FailedResponse() { var gate = new AsyncOnceGate<int>(0); BodyGates.Add(gate); gate.Fail(Failure); return gate.Task; }
    public ValueTask FailedVoid() { var gate = new AsyncOnceGate<int>(0); BodyGates.Add(gate); gate.Fail(Failure); return gate.Void; }
    public void Construct(string id, object receiver)
    {
        Core.Construct(id, receiver);
        Core.Trace.Add("new:" + id + "@" + Core.Created.Count(x => x.Value == id));
    }
    private void Pending(string point)
    {
        var pending = Branch == 2 && point == Profile + ".w0." + (Case == "acquire-wait" ? "acquire" : Case == "cleanup-wait" ? "cleanup" : "");
        Core.Pending = pending ? point : null; if (pending) PendingPoint = point;
    }
    public T Create<T>(string id, object request, CancellationToken token, Func<T> make) where T : class
    { Context(id + ".acquire"); return Core.Create(id, request, token, make); }
    public ValueTask<T> CreateAsync<T>(string id, object request, CancellationToken token, Func<T> make) where T : class
    { Context(id + ".acquire"); Pending(id + ".acquire"); return Core.CreateAsync(id, request, token, make); }
    public void Release(string id, object receiver) { Context(id + ".cleanup"); Core.Release(id, receiver); }
    public ValueTask ReleaseAsync(string id, object receiver)
    { Context(id + ".cleanup"); Pending(id + ".cleanup"); return Core.ReleaseAsync(id, receiver); }
    public void DisposeFactory(string id) => Core.DisposeFactory(id);
}

public sealed partial class GeneratorTests
{
    private static readonly string[] AllWrapperProfiles = ["SR", "SV", "AR", "AV"];
    private static string WrapperProfile(string profile) => profile switch { "SR" => "sync-response", "SV" => "sync-void", "AR" => "owned", _ => "async-void" };
    private static string HandlerMode(string profile, int id) => id == 3 ? profile == "AV" ? "async-owned" : profile == "AR" ? "owned" : "direct" :
        id == 1 ? profile == "AR" ? "async-borrowed" : profile == "SR" ? "borrowed" : "owned" :
        profile == "AR" ? "async-owned" : profile == "SR" ? "owned" : "borrowed";
    private static string AllWrapperFactory(string profile, int id)
    {
        var mode = HandlerMode(profile, id); if (mode == "direct") return "";
        var async = mode.StartsWith("async-", StringComparison.Ordinal);
        return $$"""
            public sealed class F{{id}}:I{{(async ? "Async" : "")}}{{(mode.EndsWith("owned", StringComparison.Ordinal) ? "Owned" : "Borrowed")}}SessionFactory<Ping,H{{id}}>,IDisposable {
                private readonly Probe _p;public F{{id}}(Probe p){_p=p;}
                public {{(async ? "ValueTask<H" + id + ">" : "H" + id)}} {{(async ? "CreateAsync" : "Create")}}(Ping r,CancellationToken t)=>_p.Create{{(async ? "Async" : "")}}("h{{id}}",r,t,()=>new H{{id}}(_p));
                public void Dispose()=>_p.DisposeFactory("h{{id}}");
            }
            """;
    }
    private static string AllWrapperModule(string profile)
    {
        var async = profile[0] == 'A'; var isVoid = profile[1] == 'V';
        var roles = WrapperHookRoles(async, false, WrapperProfile(profile)); var patterns = WrapperHookPatterns(async, false);
        var original = WrapperHookSource(async, isVoid, false, WrapperProfile(profile));
        var root = CSharpSyntaxTree.ParseText(original).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().ToArray();
        var handler = ns.SelectMany(n => n.Members).OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Handler").ToFullString();
        var text = "";
        foreach (var n in ns)
        {
            var kept = n.Members.Where(m => m is not ClassDeclarationSyntax c || c.Identifier.ValueText is not ("Handler" or "HF" or "Oracle" or "Reads" or "Bridge"));
            text += n.WithName(SyntaxFactory.ParseName("WrapCases." + profile)).WithUsings(root.Usings).WithMembers(SyntaxFactory.List(kept)).NormalizeWhitespace().ToFullString();
        }
        text = text.Replace("global::Zendiator.Generator.Tests.NestedWrapperProbe", "global::Zendiator.Generator.Tests.AllWrapperProbe", StringComparison.Ordinal);
        var call = async ? "InvokeAsync" : "Invoke"; var awaitCall = async ? "await " : ""; var cfg = async ? ".ConfigureAwait(false)" : "";
        var next = awaitCall + "next." + call + "(_p.NullNext==\"w0\"?null!:r,token)" + cfg;
        // Both engines call this same user wrapper, including a caught failure and a changed retry request.
        var parsed = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        var wrapper = parsed.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "W0");
        var old = wrapper.ToFullString(); var compact = Regex.Replace(old, @"\s+", "");
        var one = (isVoid ? "" : "value=") + next + ";";
        var retry = "try{" + one + "}catch(Exception failure)when(_p.Retry&&ReferenceEquals(failure,_p.Failure)){_p.Hit(\"w0.caught\",r,t,failure);" +
            (isVoid ? "" : "value=") + awaitCall + "next." + call + "(new Ping(r.Value+20),token)" + cfg + ";}";
        var expected = Regex.Replace(one, @"\s+", ""); Assert.Contains(expected, compact);
        var branch = wrapper.DescendantNodes().OfType<ElseClauseSyntax>().Single();
        var replacement = wrapper.ReplaceNode(branch.Statement, SyntaxFactory.ParseStatement("{" + retry + "}")).ToFullString();
        Assert.NotEqual(old, replacement); text = text.Replace(old, replacement, StringComparison.Ordinal);
        var members = "";
        foreach (var id in new[] { 2, 1, 3 })
        {
            var h = Regex.Replace(handler, @"\bHandler\b", "H" + id).Replace("\"h", "\"h" + id, StringComparison.Ordinal).Replace("r.Value", "(r.Value+" + id + ")", StringComparison.Ordinal);
            var hit = "_p.Hit(\"h" + id + ".body\",r,t);";
            var fail = "if(_p.FailBody(\"h" + id + "\"))" + (async ? "return _p." + (isVoid ? "FailedVoid" : "FailedResponse") + "();" : "throw _p.Failure;");
            h = h.Replace(hit, hit + fail, StringComparison.Ordinal); Assert.Contains(fail, h);
            members += h + AllWrapperFactory(profile, id);
            var reference = CanonicalWrapperHookSessionReference.Source(async, isVoid, false, roles.W0, roles.W1, HandlerMode(profile, id), roles.Modes, patterns);
            reference = Regex.Replace(reference, @"\bOracle\b", "Reference" + id);
            reference = Regex.Replace(reference, @"\bHandler\b", "H" + id); reference = Regex.Replace(reference, @"\bHF\b", "F" + id);
            text += reference.Replace("namespace App", "namespace WrapCases." + profile, StringComparison.Ordinal);
        }
        var result = async ? isVoid ? "ValueTask" : "ValueTask<int[]>" : isVoid ? "void" : "int[]";
        var invocations = string.Join("", new[] { 2, 1, 3 }.Select((id, i) => (isVoid ? "" : "values[" + i + "]=") + (async ? "await " : "") + "Reference" + id + ".Send(p,r,t)" + (async ? ".ConfigureAwait(false)" : "") + ";"));
        members += "public static class ReferenceAll{public static " + result + " Run(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);" +
            (async ? "return Execute(p,r,t); }private static async " + result + " Execute(IServiceProvider p,Ping r,CancellationToken t){" : "") +
            "t.ThrowIfCancellationRequested();" + (isVoid ? "" : "var values=new int[3];") + invocations + (isVoid ? "" : "return values;") + "}}";
        if (profile == "SV") members += """
            public readonly struct ValueWrapper:ISendWrapper<Ping>{public void Invoke<N>(Ping r,N n,CancellationToken t)where N:ISendContinuation<Ping> => n.Invoke(r,t);}
            public readonly struct ValueFactory:IOwnedSessionFactory<Ping,H1>{public H1 Create(Ping r,CancellationToken t)=>throw new NotSupportedException();}
            """;
        text += "namespace WrapCases." + profile + "{using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.AllWrapperProbe;" + members + "}";
        // The original concrete reference has its own namespace-level usings in this combined input.
        text = text.Replace("namespace WrapCases." + profile + "{public static class Reference", "namespace WrapCases." + profile + "{using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Microsoft.Extensions.DependencyInjection;public static class Reference", StringComparison.Ordinal);
        return Regex.Replace(text, @"""((?:w0|h[123]|k[0-3])(?:\.[^""]*)?)""", m => "\"" + profile + "." + m.Groups[1].Value + "\"");
    }
    private static string AllWrapperConfig()
    {
        var handlers = new List<string>(); var pipelines = new List<string>();
        foreach (var (profile, alias) in AllWrapperProfiles.Zip(new[] { "T", "U", "V", "W" }))
        {
            var async = profile[0] == 'A'; var family = async ? "SendAllAsync" : "SendAllSync"; var roles = WrapperHookRoles(async, false, WrapperProfile(profile));
            var entries = new[] { 2, 1, 3 }.Select(id => HandlerMode(profile, id) == "direct" ? $"typeof({alias}.H{id})" : $"HandlerEntry.Session<{alias}.H{id},{alias}.F{id}>()");
            handlers.Add($"c.Handlers<{alias}.Ping>(DispatchFamily.{family},{string.Join(",", entries)});");
            string Hook(int i) => $"Stage.Hook<{alias}.K{i}{(roles.Modes[i] == "direct" ? "" : "," + alias + ".KF" + i)}>()";
            pipelines.Add($"c.Pipeline<{alias}.Ping>(DispatchFamily.{family},{Hook(0)},{Hook(1)},Stage.Wrapper<{alias}.W0{(roles.W0 == "direct" ? "" : "," + alias + ".WF0")}>(),{Hook(2)},{Hook(3)});");
        }
        return string.Join("", handlers.Take(2)) + "c.Handlers<A.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<A.Handler,A.Factory>());c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));" +
            string.Join("", handlers.Skip(2)) + "c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());" +
            string.Join("", pipelines.AsEnumerable().Reverse()) + "c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());c.Pipeline<A.Ping>(DispatchFamily.SendAsync,Stage.Hook<A.Hook>());";
    }
    private static string AllWrapperSource(bool gate)
    {
        var common = SendAllCompositionSource(false); common = common[..common.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var config = AllWrapperConfig();
        var negatives = gate ? string.Join("", new[] {
            ("Two",config.Replace("Stage.Wrapper<T.W0,T.WF0>()","Stage.Wrapper<T.W0,T.WF0>(),Stage.Wrapper<T.W0,T.WF0>()",StringComparison.Ordinal)),
            ("Value",config.Replace("Stage.Wrapper<U.W0>()","Stage.Wrapper<U.ValueWrapper>()",StringComparison.Ordinal)),
            ("Factory",config.Replace("HandlerEntry.Session<U.H1,U.F1>()","HandlerEntry.Session<U.H1,U.ValueFactory>()",StringComparison.Ordinal)),
            ("Async",config.Replace("Stage.Wrapper<T.W0,T.WF0>()","Stage.Wrapper<V.W0,V.WF0>()",StringComparison.Ordinal))
        }.Select(c => "[ZendiatorComposition(\"wrapper-rejected-" + c.Item1 + "\")]public sealed class Rejected" + c.Item1 + ";public static class RejectedBridge" + c.Item1 + "{public static IServiceCollection RegisterRejected" + c.Item1 + "(IServiceCollection s)=>s.AddZendiator<Rejected" + c.Item1 + ">(c=>{" + c.Item2 + "});}")) : "";
        return common + string.Join("", AllWrapperProfiles.Select(AllWrapperModule)) + """
            namespace AllWrap{
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Zendiator.Generator.Tests;
            using T=global::WrapCases.SR;using U=global::WrapCases.SV;using V=global::WrapCases.AR;using W=global::WrapCases.AV;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;
            [ZendiatorComposition("all-wrapper")]public sealed class Many;
            public static class Bridge{
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                __CONSUMER__
            }
            __NEGATIVE__
            }
            """.Replace("__CONFIG__", config).Replace("__NEGATIVE__", negatives).Replace("__CONSUMER__", gate ? "" : """
                public static object? Dispatch(object mediator,string profile,object request,CancellationToken t,bool span){
                    var m=(global::AllWrap.Generated.Many.IZendiator)mediator;
                    switch(profile){
                        case "SR":if(span){var values=new[]{701,702,703};try{m.SendAll((T.Ping)request,values,t);}catch{if(!System.Linq.Enumerable.SequenceEqual(values,new[]{701,702,703}))throw new InvalidOperationException("Span changed before all branches succeeded");throw;}return values;}return m.SendAll((T.Ping)request,t);
                        case "SV":m.SendAll((U.Ping)request,t);return null;
                        case "AR":return m.SendAllAsync((V.Ping)request,t);
                        default:return m.SendAllAsync((W.Ping)request,t);
                    }
                }
                public static async Task<string> Interleave(object mediator,ManagedShapeProbe p,PublicValueRequestProbe stream){
                    var m=(global::AllWrap.Generated.Many.IZendiator)mediator;var result=m.Send(new R.Ping(10),p.Token.Token);
                    var values=new System.Collections.Generic.List<int>();await foreach(var value in m.StreamAsync(new S.Items(new S.SharedState())))values.Add(value);
                    if(result.A!=12||!System.Linq.Enumerable.SequenceEqual(values,new[]{2,4})||stream.SourceDisposals!=1)throw new InvalidOperationException("interleave");
                    return result.A+":"+string.Join(",",values);
                }
                """);
    }
    private static object AssertAllWrapperRegistration(Compilation output)
    {
        var registrar = output.GetTypeByMetadataName("AllWrap.Generated.Many.ZendiatorGeneratedRegistrar")!; Assert.NotNull(registrar);
        var declaration = registrar.DeclaringSyntaxReferences.Single().GetSyntax();
        var text = declaration.ToFullString();
        foreach (var profile in AllWrapperProfiles)
            foreach (var id in new[] { 2, 1, 3 })
                Assert.Contains(HandlerMode(profile, id) == "direct" ? $"typeof(global::WrapCases.{profile}.H{id})" : $"global::Zendiator.HandlerEntry.Session<global::WrapCases.{profile}.H{id}, global::WrapCases.{profile}.F{id}>()", text);
        var mediator = output.GetTypeByMetadataName("AllWrap.Generated.Many.Zendiator")!; Assert.NotNull(mediator);
        var handlers = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => c.Expression is
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "expected" }, Name: GenericNameSyntax { Identifier.ValueText: "Handlers" } }).ToArray();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var names = new List<string>();
        foreach (var entry in mediator.GetMembers("SendAll").Concat(mediator.GetMembers("SendAllAsync")).OfType<IMethodSymbol>())
        {
            var owner = CanonicalRouteEntry(output, entry).ContainingType;
            if (!seen.Add(owner)) continue;
            var request = entry.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var selected = Assert.Single(handlers, c => ((GenericNameSyntax)((MemberAccessExpressionSyntax)c.Expression).Name).TypeArgumentList.Arguments[0].ToString() == request);
            var continuations = owner.GetTypeMembers().Where(t => t.TypeKind == TypeKind.Struct && t.Name.StartsWith("WrapperNext", StringComparison.Ordinal)).ToArray();
            Assert.Equal(selected.ArgumentList.Arguments.Count - 1, continuations.Length);
            Assert.Equal(continuations.Length, continuations.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
            names.AddRange(continuations.Select(t => t.ToDisplayString()));
        }
        return new { continuationNames = names, wholeCompositionRoutes = 7 };
    }
    [Fact]
    public void AllWrapper_gate_enforces_the_single_class_wrapper_boundary_and_whole_composition_rejection()
    {
        var source = AllWrapperSource(true); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllWrapperGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllWrapperFolder("gate"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var registration = AssertAllWrapperRegistration(output);
        foreach (var name in new[] { "Two", "Value", "Factory", "Async" })
        {
            var start = source.IndexOf("public static IServiceCollection RegisterRejected" + name + "(", StringComparison.Ordinal);
            var end = source.IndexOf("});}", start, StringComparison.Ordinal) + 4;
            Assert.Contains(diagnostics, d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
            Assert.Null(output.GetTypeByMetadataName("AllWrap.Generated.Rejected" + name + ".IZendiator"));
            Assert.Null(output.GetTypeByMetadataName("AllWrap.Generated.Rejected" + name + ".ZendiatorGeneratedRegistrar"));
        }
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { registration, generatorInputs = 1, dispatches = 0, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }) }));
    }
    private static string? AllWrapperFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var path = Path.Combine(root, "all-wrapper-" + name); Directory.CreateDirectory(path); return path;
    }
    private sealed class AllWrapperEntryContext : SynchronizationContext;
    private static async Task<(string Snapshot, string? Interleave)> ObserveAllWrapper(Assembly a, string profile, string scenario, bool generated)
    {
        var oldContext = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new AllWrapperEntryContext());
        var p = new AllWrapperProbe { Profile = profile, Case = scenario }; var sync = new ManagedShapeProbe(); var stream = new PublicValueRequestProbe();
        try
        {
            if (scenario == "cancel") p.Core.Foreign.Cancel();
            var bridge = a.GetType("AllWrap.Bridge")!; var ns = "WrapCases." + profile + "."; var requestType = a.GetType(ns + "Ping")!;
            var request = Activator.CreateInstance(requestType, [1])!;
            IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); services.AddSingleton(sync); services.AddSingleton(stream); bridge.GetMethod("Register")!.Invoke(null, [services]);
            var roles = WrapperHookRoles(profile[0] == 'A', false, WrapperProfile(profile));
            var modes = Enumerable.Range(0, 4).ToDictionary(i => profile + ".k" + i, i => roles.Modes[i]); modes.Add(profile + ".w0", roles.W0);
            foreach (var id in new[] { 2, 1, 3 }) modes.Add(profile + ".h" + id, HandlerMode(profile, id));
            foreach (var (id, mode) in modes)
            {
                var suffix = id[(profile.Length + 1)..]; var type = a.GetType(ns + (suffix[0] == 'h' ? "H" + suffix[1..] : suffix[0] == 'k' ? "K" + suffix[1..] : "W0"))!;
                var factory = a.GetType(ns + (suffix[0] == 'h' ? "F" + suffix[1..] : suffix[0] == 'k' ? "KF" + suffix[1..] : "WF0"));
                var service = mode == "direct" ? type : factory!; var d = Assert.Single(services, d => d.ServiceType == service); Assert.Equal(ServiceLifetime.Scoped, d.Lifetime); Assert.Equal(service, d.ImplementationType);
                if (mode != "direct") Assert.DoesNotContain(services, d => d.ServiceType == type);
            }
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var mediator = sp.GetRequiredService(a.GetType("AllWrap.Generated.Many.IZendiator")!); Assert.Same(mediator, sp.GetRequiredService(a.GetType("AllWrap.Generated.Many.Zendiator")!));
            object? Invoke()
            {
                try { return generated ? bridge.GetMethod("Dispatch")!.Invoke(null, [mediator, profile, request, p.Core.Token.Token, profile == "SR" && scenario is "retry" or "propagate"]) : a.GetType(ns + "ReferenceAll")!.GetMethod("Run")!.Invoke(null, [sp, request, p.Core.Token.Token]); }
                catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
            }
            AllWrapperProbe.Ambient.Value = "caller"; object? value = null; Exception? error = null; var syncThrow = false; var initial = "Success"; var final = "Success"; object? waiting = null; string? interleave = null;
            ValueTask<int[]> response = default; ValueTask command = default; var returned = false;
            try { var call = Invoke(); if (profile == "AR") response = (ValueTask<int[]>)call!; else if (profile == "AV") command = (ValueTask)call!; else value = call; returned = true; }
            catch (Exception e) { error = e; syncThrow = true; initial = final = "sync-throw"; }
            if (returned && profile[0] == 'A') initial = profile == "AR" ? response.IsCompletedSuccessfully ? "Success" : response.IsFaulted ? "Faulted" : response.IsCanceled ? "Canceled" : "Pending" : command.IsCompletedSuccessfully ? "Success" : command.IsFaulted ? "Faulted" : command.IsCanceled ? "Canceled" : "Pending";
            var ambientAtReturn = AllWrapperProbe.Ambient.Value;
            if (profile[0] == 'A') Assert.Equal("caller", ambientAtReturn);
            if (scenario is "acquire-wait" or "cleanup-wait")
            {
                var gate = p.Core.Gate!; Assert.NotNull(gate); Assert.Equal("Pending", initial); Assert.Equal(2, p.Branch); Assert.Equal(0, gate.Consumptions);
                Assert.DoesNotContain(p.Core.Created.Values, v => v == profile + ".h3");
                waiting = new { p.Branch, p.PendingPoint, trace = p.Core.Trace.ToArray(), contexts = p.Contexts.ToArray(), consumptions = gate.Consumptions };
                if (generated && scenario == "acquire-wait")
                {
                    interleave = await (Task<string>)bridge.GetMethod("Interleave")!.Invoke(null, [mediator, sync, stream])!;
                    Assert.Equal("12:2,4", interleave); Assert.Equal(2, p.Branch); Assert.Equal(0, gate.Consumptions); Assert.False(response.IsCompleted);
                }
                AllWrapperProbe.Ambient.Value = "completer"; gate.Complete(); AllWrapperProbe.Ambient.Value = "caller";
            }
            if (returned && profile[0] == 'A') try { if (profile == "AR") value = await response; else await command; } catch (Exception e) { error = e; }
            if (returned && profile[0] == 'A') final = profile == "AR" ? response.IsCompletedSuccessfully ? "Success" : response.IsCanceled ? "Canceled" : "Faulted" : command.IsCompletedSuccessfully ? "Success" : command.IsCanceled ? "Canceled" : "Faulted";
            string Error(Exception? e) => e == null ? "none" : ReferenceEquals(e, p.Failure) ? "sentinel" : e is OperationCanceledException c && c.CancellationToken == p.Core.Foreign.Token ? "foreign-cancel" : e.GetType().Name;
            var failed = scenario is "propagate" or "cancel";
            Assert.Equal(failed ? 2 : 3, p.Branch);
            if (scenario == "propagate") Assert.Same(p.Failure, error); else if (scenario == "cancel") Assert.Equal(p.Core.Foreign.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken); else Assert.Null(error);
            if (failed) Assert.DoesNotContain(p.Core.Created.Values, id => id == profile + ".h3");
            if (scenario == "retry") { Assert.Equal(2, p.Core.Calls.Count(c => c.Phase == profile + ".h1.body")); Assert.Single(p.Core.Calls, c => c.Phase == profile + ".w0.caught"); if (profile == "AR") Assert.Equal(1, Assert.Single(p.BodyGates).Consumptions); }
            if (scenario == "skip") Assert.DoesNotContain(p.Core.Calls, c => c.Phase == profile + ".h1.body");
            if (p.Core.Gate != null) Assert.Equal(1, p.Core.Gate.Consumptions);
            Assert.All(p.BodyGates, g => Assert.Equal(1, g.Consumptions));
            foreach (var receiver in p.Core.Created) Assert.Equal(modes[receiver.Value].EndsWith("owned", StringComparison.Ordinal), p.Core.Released.Contains(receiver.Key));
            Assert.Empty(p.Core.FactoryDisposals);
            var beforeScope = new { released = p.Core.Released.Select(r => p.Core.Created[r]).ToArray(), ambient = AllWrapperProbe.Ambient.Value };
            p.Case = ""; p.Core.Pending = null; await scope.DisposeAsync();
            foreach (var receiver in p.Core.Created) Assert.Equal(modes[receiver.Value] == "direct" || modes[receiver.Value].EndsWith("owned", StringComparison.Ordinal), p.Core.Released.Contains(receiver.Key));
            Assert.All(p.Core.FactoryDisposals, d => Assert.Equal(1, d.Value));
            var snapshot = JsonSerializer.Serialize(new
            {
                profile,
                scenario,
                value,
                error = Error(error),
                syncThrow,
                returned,
                initial,
                final,
                p.Branch,
                ambientAtReturn,
                p.Contexts,
                p.Core.Trace,
                calls = p.Core.Calls.Select(c => new { c.Phase, value = requestType.GetProperty("Value")!.GetValue(c.Request), token = c.Token == p.Core.Foreign.Token ? "foreign" : c.Token == p.Core.Token.Token ? "original" : "wrong", error = Error(c.Error), success = c.Exit?.Success, exitError = Error(c.Exit?.Error) }),
                created = p.Core.Created.Values.ToArray(),
                released = p.Core.Released.Select(r => p.Core.Created[r]).ToArray(),
                factories = p.Core.FactoryDisposals,
                beforeScope,
                waiting,
                consumptions = p.Core.Gate?.Consumptions ?? 0,
                failedNextConsumptions = p.BodyGates.Select(g => g.Consumptions).ToArray()
            });
            var folder = AllWrapperFolder("runtime"); if (folder != null) File.WriteAllText(Path.Combine(folder, profile + "-" + scenario + "-" + generated + ".json"), snapshot);
            foreach (var receiver in p.Core.Created.Keys.Where(r => !p.Core.Released.Contains(r)).ToArray()) { if (profile[0] == 'A') await ((IAsyncDisposable)receiver).DisposeAsync(); else ((IDisposable)receiver).Dispose(); }
            Assert.Equal(p.Core.Created.Count, p.Core.Released.Count);
            return (snapshot, interleave);
        }
        finally { p.Core.Token.Dispose(); p.Core.Foreign.Dispose(); sync.Token.Dispose(); AllWrapperProbe.Ambient.Value = null; SynchronizationContext.SetSynchronizationContext(oldContext); }
    }
    [Fact]
    public async Task AllWrapper_runtime_preserves_retry_short_circuit_propagation_waits_and_ownership()
    {
        var source = AllWrapperSource(false); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllWrapperRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllWrapperFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var registration = AssertAllWrapperRegistration(output);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "registration.json"), JsonSerializer.Serialize(registration)); }
        bytes.Position = 0; var a = AssemblyLoadContext.Default.LoadFromStream(bytes); var rows = new List<object>(); string? interleave = null;
        foreach (var (profile, scenario) in new[] { ("SR", "retry"), ("SV", "skip"), ("AR", "acquire-wait"), ("AV", "success"), ("AR", "retry"), ("AV", "skip"), ("SR", "propagate"), ("AR", "propagate"), ("AV", "cancel"), ("AR", "cleanup-wait") })
        {
            var generated = await ObserveAllWrapper(a, profile, scenario, true); var reference = await ObserveAllWrapper(a, profile, scenario, false);
            rows.Add(new { profile, scenario, generated = JsonDocument.Parse(generated.Snapshot).RootElement.Clone(), reference = JsonDocument.Parse(reference.Snapshot).RootElement.Clone() });
            if (generated.Interleave != null) interleave = generated.Interleave;
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), JsonSerializer.Serialize(new { generatorInputs = 1, completedPairedScenarios = rows.Count, pairedDispatches = rows.Count * 2, interleaveDispatches = interleave == null ? 0 : 2, rows, interleave }));
            Assert.Equal(reference.Snapshot, generated.Snapshot);
        }
    }
}

