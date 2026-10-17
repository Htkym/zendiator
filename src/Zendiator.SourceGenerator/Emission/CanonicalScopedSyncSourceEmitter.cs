using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalScopedSyncSourceEmitter
{
    internal static void Emit(StringBuilder b, CanonicalSyncCompositionEmission c)
    {
        var h = c.Handler; var o = c.ScopedObserver!; var result = h.ResultName ?? "void";
        string Call(BoundSyncHookCallbackEmission callback, string arguments) =>
            "((" + callback.ContractName + ")observer)." + callback.MethodName + "(" + arguments + ");";
        b.AppendLine($$"""
            namespace {{c.Namespace}} {
            public interface IZendiator { {{result}} Send(scoped {{h.RequestName}} request,global::System.Threading.CancellationToken cancellationToken=default); }
            public sealed class Zendiator:IZendiator {
                private readonly global::System.IServiceProvider _provider;
                public Zendiator(global::System.IServiceProvider provider){global::System.ArgumentNullException.ThrowIfNull(provider);_provider=provider;}
                public {{result}} Send(scoped {{h.RequestName}} request,global::System.Threading.CancellationToken cancellationToken=default){
                    {{(h.ResultName == null ? "" : "return ")}}Execute(request,cancellationToken);
                }
                private {{result}} Execute(scoped {{h.RequestName}} request,global::System.Threading.CancellationToken cancellationToken){
                    cancellationToken.ThrowIfCancellationRequested();
                    var observer={{CanonicalServiceResolution.Emit(o.ObserverName, false)}};
                    var success=false;global::System.Exception? error=null;
                    try{
                        {{Call(o.Before, "request,cancellationToken")}}
                        cancellationToken.ThrowIfCancellationRequested();
                        var handler={{CanonicalServiceResolution.Emit(h.HandlerName, false)}};
                        {{(h.ResultName == null ? "" : "var response=")}}(({{h.ContractName}})handler).{{h.MethodName}}(request,cancellationToken);
                        {{Call(o.Success, "request," + (h.ResultName == null ? "" : "response,") + "cancellationToken")}}
                        success=true;
                        {{(h.ResultName == null ? "" : "return response;")}}
                    }catch(global::System.Exception failure){
                        error=failure;
                        try{ {{Call(o.Exception, "request,failure,cancellationToken")}} }
                        catch(global::System.Exception replacement){error=replacement;throw;}
                        throw;
                    }finally{ {{Call(o.Finally, "request,new global::Zendiator.SendExitInfo(success,error),cancellationToken")}} }
                }
            }
            """);
        CanonicalSyncSourceEmitter.EmitRegistrar(b, c);
        b.AppendLine("}");
    }
}
