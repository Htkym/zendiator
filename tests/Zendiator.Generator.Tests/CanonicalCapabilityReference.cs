namespace Zendiator.Generator.Tests;

// Independent handwritten concrete oracle text, selected from fixture-declared contracts.
// No Roslyn model, graph, emitter output or runtime callback interpreter is consulted.
internal static class CanonicalCapabilityReference
{
    internal static string Source(bool async,bool isVoid,string before,string after,string observer,string final,bool two,string hookFactory="direct",string handlerFactory="direct",bool wrapper=false)
    {
        var result=async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int");var prefix=async?"async ":"";
        string Call(string kind,string sync,string asynchronous)=>kind=="a"?"await "+asynchronous+".ConfigureAwait(false)":sync;
        string Acquire(string mode,string type,string factory,string request)=>mode=="direct"?$"p.GetRequiredService<{type}>()":
            (mode.StartsWith("async-",StringComparison.Ordinal)?$"await p.GetRequiredService<{factory}>().CreateAsync({request},t).ConfigureAwait(false)":$"p.GetRequiredService<{factory}>().Create({request},t)")+"??throw new InvalidOperationException(\"A session factory must return a nonnull callback receiver.\")";
        string Dispose(string receiver)=>async?$"await {receiver}.DisposeAsync().ConfigureAwait(false);":receiver+".Dispose();";
        string Region(string tag,string input,bool inner)
        {
            var name="h"+tag;var saved="saved"+tag;var error="error"+tag;var success="success"+tag;
            var ownHook=hookFactory.EndsWith("owned",StringComparison.Ordinal);var ownHandler=handlerFactory.EndsWith("owned",StringComparison.Ordinal);
            var text=$"Hook{tag}? {name}={Acquire(hookFactory,"Hook"+tag,"KFactory",input)};"+(ownHook?"try{":"")+$"var {saved}={input};"+(final=="-"?"":$"var {success}=false;Exception? {error}=null;")+"try{";
            if(before!="-")text+=saved+"="+Call(before,$"((ISendRequestTransform<Ping>){name}).Before({saved},t)",$"((IAsyncSendRequestTransform<Ping>){name}).BeforeAsync({saved},t)")+";";
            text+=$"ArgumentNullException.ThrowIfNull({saved},\"request\");t.ThrowIfCancellationRequested();";
            if(wrapper&&!inner)text+=(isVoid?"":"response=")+(async?"await ":"")+"p.GetRequiredService<W>()."+(async?"InvokeAsync":"Invoke")+"("+saved+",new Next(p),t)"+(async?".ConfigureAwait(false)":"")+";";
            else if(two&&!inner)text+=Region("1",saved,true);
            else{
                text+=$"Handler? handler={Acquire(handlerFactory,"Handler","HFactory",saved)};"+(ownHandler?"try{":"")+(isVoid?"":"response=")+(async?$"await handler.HandleAsync({saved},t).ConfigureAwait(false)":$"handler.Handle({saved},t)")+";";
                if(ownHandler)text+="}finally{var detached=handler;handler=null;"+Dispose("detached")+"}";
            }
            if(after!="-")text+=(isVoid?"":"response=")+Call(after,isVoid?$"((ISendSuccessObserver<Ping>){name}).After({saved},t)":$"((ISendResponseTransform<Ping,int>){name}).After({saved},response,t)",isVoid?$"((IAsyncSendSuccessObserver<Ping>){name}).AfterAsync({saved},t)":$"((IAsyncSendResponseTransform<Ping,int>){name}).AfterAsync({saved},response,t)")+";";
            if(final!="-")text+=success+"=true;";
            if(observer!="-"||final!="-"){
                text+="}catch(Exception failure"+tag+"){"+(final=="-"?"":error+"=failure"+tag+";");
                if(observer!="-"){
                    var observed=Call(observer,$"((ISendExceptionObserver<Ping>){name}).OnException({saved},failure{tag},t)",$"((IAsyncSendExceptionObserver<Ping>){name}).OnExceptionAsync({saved},failure{tag},t)")+";";
                    text+=final=="-"?observed:"try{"+observed+"}catch(Exception replacement"+tag+"){"+error+"=replacement"+tag+";throw;}";
                }
                text+="throw;";
            }
            text+="}finally{";
            if(final!="-")text+=Call(final,$"((ISendFinallyObserver<Ping>){name}).Finally({saved},new({success},{error}),t)",$"((IAsyncSendFinallyObserver<Ping>){name}).FinallyAsync({saved},new({success},{error}),t)")+";";
            text+="}";if(ownHook)text+="}finally{var detached="+name+";"+name+"=null;"+Dispose("detached")+"}";return text;
        }
        var continuation="";
        if(wrapper)continuation="private readonly struct Next:"+(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping"+(isVoid?"":",int")+">{private readonly IServiceProvider _p;public Next(IServiceProvider p){_p=p;}public "+result+" "+(async?"InvokeAsync":"Invoke")+"(Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);"+(async||!isVoid?"return ":"")+"Inner(_p,r,t);}}private static "+prefix+result+" Inner(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();"+(isVoid?"":"int response;")+Region("1","r",true)+(isVoid?"":"return response;")+"}";
        return "namespace App {public static class Oracle {public static "+result+" Send(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);"+(async||!isVoid?"return ":"")+"Execute(p,r,t);}"+
            "private static "+prefix+result+" Execute(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();"+(isVoid?"":"int response;")+Region("0","r",false)+(isVoid?"":"return response;")+"}"+continuation+"}}";
    }
}
