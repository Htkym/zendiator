using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalWrapperEmissionNormalizer
{
    internal static CanonicalWrapperEmissionModel Create(CompositionSelectionCatalog catalog,CanonicalExecutionGraphCatalog execution)
    {
        var output=new List<CanonicalWrapperCompositionEmission>();
        for(var i=0;i<catalog.Compositions.Count;i++)
        {
            var c=catalog.Compositions[i];
            if(c.GeneratedNamespace==null||c.MicrosoftDiProviderName==null||c.Errors.Count!=0||c.CompilerOwnsHandlerError||c.HasExplicitSelection||c.Pipelines.Count!=1||
                c.Facts.Count!=1||c.Facts[0].Key!="IncompleteExecutionContract"||catalog.Compositions.Count(x=>x.Identity==c.Identity||x.Key==c.Key||x.GeneratedNamespace==c.GeneratedNamespace)!=1)continue;
            var route=c.Pipelines[0];var graph=execution.Compositions[i].SingleOrDefault(g=>g.Contract.Anchor==route.Anchor);
            if(graph?.IsValid!=true||graph.Schedule==null||route.Family is not (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync)||
                route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)||route.Handlers.Count!=1||!route.ExplicitHandlerOrder)continue;
            var wrappers=route.Stages.Select((s,index)=>(Stage:s,Index:index)).Where(s=>s.Stage.Kind==FusionStageKind.Wrapper).ToArray();
            if(wrappers.Length==0)continue;var w=wrappers[0];
            var sync=route.Family==BoundDispatchFamily.SendSync;var h=route.Handlers[0];
            if(h.CompilerOwnsError||!c.Handlers.Any(x=>x.Identity==h.Type)||sync&&h.SyncEmission==null||!sync&&h.AsyncEmission==null)continue;
            var regions=new List<CanonicalWrapperRegionEmission>();HookCapabilityEmission? pending=null;var invalid=false;
            foreach(var s in route.Stages){
                if(s.CompilerOwnsError||s.Sessions.Count>1){invalid=true;break;}
                if(s.Kind==FusionStageKind.Hook){if(pending!=null||s.Capabilities==null||s.Sessions.Count!=0||s.ServiceOwnership!=FusionOwnership.BorrowedService){invalid=true;break;}pending=s.Capabilities;continue;}
                if(s.Kind!=FusionStageKind.Wrapper||s.WrapperEmission==null||s.WrapperEmission.Completion!=(sync?BoundCompletionKind.Synchronous:BoundCompletionKind.ValueTask)){invalid=true;break;}
                var factory=s.Sessions.Count==0?null:CanonicalCapabilityEmissionNormalizer.Factory(s.Sessions[0],sync);
                if(s.Sessions.Count==0?s.ServiceOwnership!=FusionOwnership.BorrowedService:factory==null||s.ServiceOwnership!=s.Sessions[0].Ownership){invalid=true;break;}
                regions.Add(new(pending,s.WrapperEmission,factory));pending=null;
            }
            if(invalid||h.Sessions.Count>1)continue;regions.Add(new(pending,null,null));
            var generalized=wrappers.Length!=1||w.Stage.Sessions.Count!=0;
            var outer=!generalized&&w.Index==1?route.Stages[0]:null;var inner=!generalized&&w.Index+1<route.Stages.Count?route.Stages[w.Index+1]:null;
            BoundSyncFactoryEmission? sf=null;BoundAsyncFactoryEmission? af=null;
            if(h.Sessions.Count==1)
            {
                var s=h.Sessions[0];
                if(!s.SuppliesCallbackReceiver||s.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource))continue;
                if(sync)
                {
                    sf=s.SyncEmission;
                    if(sf is not {IsSourceDeclaration:true}||s.Acquire.Completion!=BoundCompletionKind.Synchronous||
                        s.Ownership==FusionOwnership.OwnedSession&&(sf.CleanupContractName==null||s.Cleanup?.Completion!=BoundCompletionKind.Synchronous)||
                        s.Ownership==FusionOwnership.BorrowedResource&&(sf.CleanupContractName!=null||s.Cleanup!=null))continue;
                }
                else
                {
                    af=s.AsyncEmission;
                    if(af is not {IsSourceDeclaration:true}||s.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask)||
                        s.Ownership==FusionOwnership.OwnedSession&&(af.CleanupContractName==null||s.Cleanup?.Completion!=BoundCompletionKind.ValueTask)||
                        s.Ownership==FusionOwnership.BorrowedResource&&(af.CleanupContractName!=null||s.Cleanup!=null))continue;
                    sf=new(af.FactoryName,af.ContractName,af.MethodName,af.CleanupContractName);
                }
            }
            else if(h.ServiceOwnership!=FusionOwnership.BorrowedService)continue;
            var handler=sync?h.SyncEmission!:new BoundSyncHandlerEmission(h.AsyncEmission!.RequestName,h.AsyncEmission.HandlerName,h.AsyncEmission.ContractName,h.AsyncEmission.MethodName,h.AsyncEmission.ResultName);
            var registration=new CanonicalSyncCompositionEmission(i,c.GeneratedNamespace,c.MarkerName,c.UseLocalHandlers,handler,sf,false,route.PipelineFamily,c.MicrosoftDiProviderName)
            {RegistrationStages=new(route.Stages.Select(s=>new RegistrationStageEmission(s.Name,s.Kind==FusionStageKind.Wrapper?"Wrapper":"Hook",s.Sessions.Count==0?null:CanonicalCapabilityEmissionNormalizer.Factory(s.Sessions[0],sync)!.FactoryName)))};
            output.Add(new(registration,route.Family.Value,w.Stage.WrapperEmission!,outer?.SyncEmission,inner?.SyncEmission,outer?.AsyncEmission,inner?.AsyncEmission,af)
            {OuterCapabilities=outer!=null&&(sync?outer.SyncEmission==null:outer.AsyncEmission==null)?outer.Capabilities:null,
                InnerCapabilities=inner!=null&&(sync?inner.SyncEmission==null:inner.AsyncEmission==null)?inner.Capabilities:null,Regions=generalized?new(regions):default});
        }
        return new(new(output));
    }
    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalWrapperEmissionModel model,ImmutableArray<CompositionSelectionDiscovery.Binding> current,Compilation compilation,CancellationToken token)=>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c=>c.Registration))),current,compilation,token);
}
