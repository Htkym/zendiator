namespace Zendiator.Generator.Tests;

// Independent concrete fixture contracts. No bound model, graph or emitted body is consulted.
internal static class CanonicalWrapperHookSessionReference
{
    internal static string Source(bool async,bool isVoid,bool two,string w0,string w1,string handler,string[] modes,string[] patterns,bool standalone=false,bool explicitObserver=false)
    {
        var result=async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int");var groups=standalone?new[]{new[]{0,1,2,3}}:two?new[]{new[]{0,1},new[]{2},new[]{3,4}}:new[]{new[]{0,1},new[]{2,3}};
        string Acquire(string mode,string type,string factory,string r)=>mode=="direct"?$"p.GetRequiredService<{type}>()":(mode.StartsWith("async-",StringComparison.Ordinal)?$"await p.GetRequiredService<{factory}>().CreateAsync({r},t).ConfigureAwait(false)":$"p.GetRequiredService<{factory}>().Create({r},t)")+"??throw new InvalidOperationException(\"A session factory must return a nonnull callback receiver.\")";
        string Cleanup(string v)=>async?$"await {v}.DisposeAsync().ConfigureAwait(false);":v+".Dispose();";
        string Await(string call,bool a)=>a?"await "+call+".ConfigureAwait(false)":call;
        var text="namespace App{public static class Oracle{public static "+result+" Send(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);"+(async||!isVoid?"return ":"")+"Region0(p,r,t);}";
        for(var region=0;region<groups.Length;region++){
            string Work(int at,string input){
                if(at==groups[region].Length){var leaf=region==groups.Length-1;var mode=leaf?handler:region==0?w0:w1;var type=leaf?"Handler":"W"+region;var factory=leaf?"HF":"WF"+region;var leafReceiver=leaf?"h":"w";var leafOwned=mode.EndsWith("owned",StringComparison.Ordinal);var leafBody=type+"? "+leafReceiver+"="+Acquire(mode,type,factory,input)+";"+(leafOwned?"try{":"")+(isVoid?"":"response=")+Await(leafReceiver+"."+(leaf?(async?"HandleAsync":"Handle"):(async?"InvokeAsync":"Invoke"))+"("+input+(leaf?"":",new Next"+region+"(p)")+",t)",async)+";";return leafBody+(leafOwned?"}finally{var detached="+leafReceiver+";"+leafReceiver+"=null;"+Cleanup("detached")+"}":"");}
                var i=groups[region][at];var v="k"+i;var saved="saved"+i;var pattern=patterns[i];var owned=modes[i].EndsWith("owned",StringComparison.Ordinal);var body=$"K{i}? {v}={Acquire(modes[i],"K"+i,"KF"+i,input)};"+(owned?"try{":"")+$"var {saved}={input};"+(pattern[3]=='-'?"":$"var ok{i}=false;Exception? error{i}=null;")+"try{";
                if(pattern[0]!='-')body+=saved+"="+Await(v+"."+(pattern[0]=='a'?"BeforeAsync":"Before")+"("+saved+",t)",pattern[0]=='a')+";";
                body+=$"ArgumentNullException.ThrowIfNull({saved},\"request\");t.ThrowIfCancellationRequested();"+Work(at+1,saved);
                if(pattern[1]!='-')body+=(isVoid?"":"response=")+Await(v+"."+(pattern[1]=='a'?"AfterAsync":"After")+"("+saved+(isVoid?"":",response")+",t)",pattern[1]=='a')+";";
                if(pattern[3]!='-')body+=$"ok{i}=true;";
                if(pattern[2]!='-'||pattern[3]!='-'){body+=$"}}catch(Exception failure{i}){{"+(pattern[3]=='-'?"":$"error{i}=failure{i};");if(pattern[2]!='-'){var observer=explicitObserver&&i==1?"(("+(pattern[2]=='a'?"IAsyncSendExceptionObserver":"ISendExceptionObserver")+"<Ping>)"+v+")":v;var observe=Await(observer+"."+(pattern[2]=='a'?"OnExceptionAsync":"OnException")+$"({saved},failure{i},t)",pattern[2]=='a')+";";body+=pattern[3]=='-'?observe:$"try{{{observe}}}catch(Exception replacement{i}){{error{i}=replacement{i};throw;}}";}body+="throw;";}
                body+="}finally{";if(pattern[3]!='-')body+=Await(v+"."+(pattern[3]=='a'?"FinallyAsync":"Finally")+$"({saved},new(ok{i},error{i}),t)",pattern[3]=='a')+";";body+="}";if(owned)body+="}finally{var detached="+v+";"+v+"=null;"+Cleanup("detached")+"}";return body;
            }
            text+="private static "+(async?"async ":"")+result+" Region"+region+"(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();"+(isVoid?"":"int response;")+Work(0,"r")+(isVoid?"":"return response;")+"}";
            if(region!=groups.Length-1)text+="private readonly struct Next"+region+":"+(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping"+(isVoid?"":",int")+">{private readonly IServiceProvider _p;public Next"+region+"(IServiceProvider p){_p=p;}public "+result+" "+(async?"InvokeAsync":"Invoke")+"(Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);"+(async||!isVoid?"return ":"")+"Region"+(region+1)+"(_p,r,t);}}";
        }
        return text+"}}";
    }
}
