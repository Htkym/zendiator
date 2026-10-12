using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
namespace Zendiator.SourceGenerator;
internal static class CanonicalCapabilityEmissionNormalizer
{
    internal static CanonicalCapabilityEmissionModel Create(CompositionSelectionCatalog catalog,CanonicalExecutionGraphCatalog execution,
        CanonicalSyncEmissionModel syncModel,CanonicalAsyncEmissionModel asyncModel,CanonicalWrapperEmissionModel wrapperModel)
    {
        var output=new List<CanonicalCapabilityCompositionEmission>();
        for(var i=0;i<catalog.Compositions.Count;i++)
        {
            if(syncModel.Compositions.Any(c=>c.Index==i)||asyncModel.Compositions.Any(c=>c.Index==i)||wrapperModel.Compositions.Any(c=>c.Registration.Index==i))continue;
            var c=catalog.Compositions[i];
            if(c.GeneratedNamespace==null||c.MicrosoftDiProviderName==null||c.Errors.Count!=0||c.CompilerOwnsHandlerError||c.HasExplicitSelection||c.Pipelines.Count!=1||
                c.Facts.Count!=1||c.Facts[0].Key!="IncompleteExecutionContract"||catalog.Compositions.Count(x=>x.Identity==c.Identity||x.Key==c.Key||x.GeneratedNamespace==c.GeneratedNamespace)!=1)continue;
            var route=c.Pipelines[0];var graph=execution.Compositions[i].SingleOrDefault(g=>g.Contract.Anchor==route.Anchor);
            if(graph?.IsValid!=true||graph.Schedule==null||route.Family is not (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync)||
                route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)||route.Handlers.Count!=1||!route.ExplicitHandlerOrder||route.Stages.Count==0||route.Stages.Count>2)continue;
            var sync=route.Family==BoundDispatchFamily.SendSync;var h=route.Handlers[0];
            if(h.CompilerOwnsError||!c.Handlers.Any(x=>x.Identity==h.Type)||sync&&h.SyncEmission==null||!sync&&h.AsyncEmission==null||h.Sessions.Count!=0)continue;
            // Partial/mixed callbacks are proved here with borrowed DI receivers only.
            // Existing complete-capability session routes keep their established emitter.
            if(route.Stages.Any(s=>s.Kind!=FusionStageKind.Hook||s.CompilerOwnsError||s.Capabilities==null||s.Sessions.Count!=0))continue;
            if(route.Stages.Any(s=>s.ServiceOwnership!=FusionOwnership.BorrowedService)||h.ServiceOwnership!=FusionOwnership.BorrowedService)continue;
            var hooks=route.Stages.Select(s=>s.Capabilities!).ToArray();
            var handler=sync?h.SyncEmission!:new BoundSyncHandlerEmission(h.AsyncEmission!.RequestName,h.AsyncEmission.HandlerName,h.AsyncEmission.ContractName,h.AsyncEmission.MethodName,h.AsyncEmission.ResultName);
            var registration=new CanonicalSyncCompositionEmission(i,c.GeneratedNamespace,c.MarkerName,c.UseLocalHandlers,handler,
                null,false,route.PipelineFamily,c.MicrosoftDiProviderName)
            {RegistrationStages=new(hooks.Select(s=>new RegistrationStageEmission(s.HookName,"Hook")))};
            output.Add(new(registration,route.Family.Value,new(hooks)));
        }
        return new(new(output));
    }
    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalCapabilityEmissionModel model,ImmutableArray<CompositionSelectionDiscovery.Binding> current,Compilation compilation,CancellationToken token)=>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c=>c.Registration))),current,compilation,token);
}
