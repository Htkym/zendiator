using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalAsyncFailureDataEmitter
{
    internal static void Emit(StringBuilder b, CanonicalCapabilityCompositionEmission c, CanonicalRouteDependenciesPlan dependencies)
    {
        b.AppendLine($"{c.Registration.Handler.ResultName} response;");
        EmitScope(b, c, dependencies, 0, "request");
        b.AppendLine("return response;");
    }

    private static void EmitScope(StringBuilder b, CanonicalCapabilityCompositionEmission c, CanonicalRouteDependenciesPlan dependencies,
        int index, string input)
    {
        var h = c.Registration.Handler;
        var hook = c.Hooks[index];
        var receiver = "hook" + index;
        var saved = "saved" + index;
        var error = "error" + index;
        var success = "success" + index;
        var failure = "failure" + index;
        var propagate = "propagate" + index;
        var complete = "complete" + index;
        b.AppendLine($$"""
            {
                var {{receiver}} = {{CanonicalRouteDependenciesEmitter.Receiver(dependencies, hook.HookName, hook.HookIsValueType)}};
                var {{saved}} = {{input}};
                var {{success}} = false;
                global::System.Exception? {{error}} = null;
                try
                {
                    {{saved}} = await Before{{index}}({{receiver}}, {{saved}}, cancellationToken).ConfigureAwait(false);
                    {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(" + saved + ", \"request\");")}}
                    cancellationToken.ThrowIfCancellationRequested();
            """);
        if (index + 1 < c.Hooks.Count)
            EmitScope(b, c, dependencies, index + 1, saved);
        else
            b.AppendLine($$"""
                    var handler = {{CanonicalRouteDependenciesEmitter.Receiver(dependencies, h.HandlerName, h.HandlerIsValueType)}};
                    response = await Handle(handler, {{saved}}, cancellationToken).ConfigureAwait(false);
                """);
        b.AppendLine($$"""
                    response = await After{{index}}({{receiver}}, {{saved}}, response, cancellationToken).ConfigureAwait(false);
                    {{success}} = true;
                }
                catch (global::System.Exception caught{{index}}) when (caught{{index}} is not null)
                {
                    {{error}} = caught{{index}};
                    goto {{failure}};
                }
                catch
                {
                    // Preserve the existing finally boundary for non-CLS throws.
                    await Finally{{index}}({{receiver}}, {{saved}}, new global::Zendiator.SendExitInfo({{success}}, {{error}}), cancellationToken).ConfigureAwait(false);
                    throw;
                }
                try
                {
                    await Finally{{index}}({{receiver}}, {{saved}}, new global::Zendiator.SendExitInfo({{success}}, {{error}}), cancellationToken).ConfigureAwait(false);
                }
                catch (global::System.Exception finalFailure{{index}})
                {
                    {{error}} = finalFailure{{index}};
                    goto {{propagate}};
                }
                goto {{complete}};
            {{failure}}:
                try
                {
                    await Observe{{index}}({{receiver}}, {{saved}}, {{error}}!, cancellationToken).ConfigureAwait(false);
                }
                catch (global::System.Exception replacement{{index}}) when (replacement{{index}} is not null) { {{error}} = replacement{{index}}; }
                catch
                {
                    await Finally{{index}}({{receiver}}, {{saved}}, new global::Zendiator.SendExitInfo({{success}}, {{error}}), cancellationToken).ConfigureAwait(false);
                    throw;
                }
                try
                {
                    await Finally{{index}}({{receiver}}, {{saved}}, new global::Zendiator.SendExitInfo({{success}}, {{error}}), cancellationToken).ConfigureAwait(false);
                }
                catch (global::System.Exception finalReplacement{{index}}) { {{error}} = finalReplacement{{index}}; }
            {{propagate}}:
            """);
        if (index != 0)
            b.AppendLine($$"""
                error{{index - 1}} = {{error}};
                goto failure{{index - 1}};
                """);
        else
            b.AppendLine($$"""
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture({{error}}!).Throw();
                throw new global::System.InvalidOperationException("ExceptionDispatchInfo.Throw returned unexpectedly.");
                """);
        b.AppendLine($$"""
            {{complete}}:;
            }
            """);
    }
}
