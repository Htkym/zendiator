namespace Zendiator.Generator.Tests;

// Concrete fixture contracts only; no generator model, schedule or generated source is read.
internal static class CanonicalNestedWrapperReference
{
    internal static string Source(bool async, bool isVoid, bool two, string w0, string w1, string handler, bool hooks)
    {
        var result = async ? (isVoid ? "ValueTask" : "ValueTask<int>") : (isVoid ? "void" : "int"); var last = two ? 2 : 1;
        string Acquire(string mode, string type, string factory, string r) => mode == "direct" ? $"p.GetRequiredService<{type}>()" : (mode.StartsWith("async-", StringComparison.Ordinal) ? $"await p.GetRequiredService<{factory}>().CreateAsync({r},t).ConfigureAwait(false)" : $"p.GetRequiredService<{factory}>().Create({r},t)") + "??throw new InvalidOperationException(\"A session factory must return a nonnull callback receiver.\")";
        string Await(string call) => async ? "await " + call + ".ConfigureAwait(false)" : call;
        var text = "namespace App{public static class Oracle{public static " + result + " Send(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);" + (async || !isVoid ? "return " : "") + "Run0(p,r,t);}";
        for (var i = 0; i <= last; i++)
        {
            var mode = i == last ? handler : i == 0 ? w0 : w1; var type = i == last ? "Handler" : "W" + i; var factory = i == last ? "HF" : "WF" + i; var variable = i == last ? "h" : "w"; var owned = mode.EndsWith("owned", StringComparison.Ordinal);
            text += "private static " + (async ? "async " : "") + result + " Run" + i + "(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();";
            if (hooks) text += $"var k=p.GetRequiredService<K{i}>();var saved=r;var ok=false;Exception? error=null;try{{saved=k.Before(saved,t);ArgumentNullException.ThrowIfNull(saved,\"request\");t.ThrowIfCancellationRequested();";
            var input = hooks ? "saved" : "r"; text += (isVoid ? "" : "int value;") + type + "? " + variable + "=" + Acquire(mode, type, factory, input) + ";" + (owned ? "try{" : "");
            text += (isVoid ? "" : "value=") + Await(variable + "." + (i == last ? (async ? "HandleAsync" : "Handle") : (async ? "InvokeAsync" : "Invoke")) + "(" + input + (i == last ? "" : ",new Next" + i + "(p)") + ",t)") + ";";
            if (owned) text += "}finally{var detached=" + variable + ";" + variable + "=null;" + (async ? "await detached.DisposeAsync().ConfigureAwait(false);" : "detached.Dispose();") + "}";
            if (hooks) { text += (isVoid ? "" : "value=") + (async ? "await k.AfterAsync(" + input + (isVoid ? "" : ",value") + ",t).ConfigureAwait(false)" : "k.After(" + input + (isVoid ? "" : ",value") + ",t)") + ";ok=true;"; }
            if (!isVoid) text += "return value;";
            if (hooks) text += "}catch(Exception e){error=e;try{" + (async ? "await k.OnExceptionAsync(saved,e,t).ConfigureAwait(false);" : "k.OnException(saved,e,t);") + "}catch(Exception replacement){error=replacement;throw;}throw;}finally{k.Finally(saved,new(ok,error),t);}";
            text += "}";
            if (i != last) text += "private readonly struct Next" + i + ":" + (async ? "IAsyncSendContinuation" : "ISendContinuation") + "<Ping" + (isVoid ? "" : ",int") + ">{private readonly IServiceProvider _p;public Next" + i + "(IServiceProvider p){_p=p;}public " + result + " " + (async ? "InvokeAsync" : "Invoke") + "(Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);" + (async || !isVoid ? "return " : "") + "Run" + (i + 1) + "(_p,r,t);}}";
        }
        return text + "}}";
    }
}
