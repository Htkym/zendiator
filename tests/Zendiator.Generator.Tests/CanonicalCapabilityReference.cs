namespace Zendiator.Generator.Tests;

// Independent handwritten concrete oracle text, selected from fixture-declared contracts.
// No Roslyn model, graph, emitter output or runtime callback interpreter is consulted.
internal static class CanonicalCapabilityReference
{
    internal static string Source(bool async,bool isVoid,string before,string after,string observer,string final,bool two)
    {
        var result=async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int");var prefix=async?"async ":"";
        string Call(string kind,string sync,string asynchronous)=>kind=="a"?"await "+asynchronous+".ConfigureAwait(false)":sync;
        string Region(string tag,string input,bool inner)
        {
            var name="h"+tag;var saved="saved"+tag;var error="error"+tag;var success="success"+tag;
            var text=$"var {name}=p.GetRequiredService<Hook{tag}>();var {saved}={input};"+(final=="-"?"":$"var {success}=false;Exception? {error}=null;")+"try{";
            if(before!="-")text+=saved+"="+Call(before,$"((ISendRequestTransform<Ping>){name}).Before({saved},t)",$"((IAsyncSendRequestTransform<Ping>){name}).BeforeAsync({saved},t)")+";";
            text+=$"ArgumentNullException.ThrowIfNull({saved},\"request\");t.ThrowIfCancellationRequested();";
            if(two&&!inner)text+=Region("1",saved,true);
            else text+=(isVoid?"":"response=")+(async?$"await p.GetRequiredService<Handler>().HandleAsync({saved},t).ConfigureAwait(false)":$"p.GetRequiredService<Handler>().Handle({saved},t)")+";";
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
            return text+"}";
        }
        return "namespace App {public static class Oracle {public static "+result+" Send(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);"+(async||!isVoid?"return ":"")+"Execute(p,r,t);}"+
            "private static "+prefix+result+" Execute(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();"+(isVoid?"":"int response;")+Region("0","r",false)+(isVoid?"":"return response;")+"}}}";
    }
}
