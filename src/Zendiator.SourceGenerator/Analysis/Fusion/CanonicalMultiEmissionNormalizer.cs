using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
namespace Zendiator.SourceGenerator;

internal static class CanonicalMultiEmissionNormalizer
{
    internal static CanonicalMultiEmissionModel Create(CompositionSelectionCatalog catalog,CanonicalExecutionGraphCatalog execution)
    {
        var result=new List<CanonicalMultiCompositionEmission>();
        for(var i=0;i<catalog.Compositions.Count;i++)
        {
            var c=catalog.Compositions[i];
            if(c.Pipelines.Count<2||c.GeneratedNamespace==null||c.Errors.Count!=0||c.CompilerOwnsHandlerError||
                c.Facts.Count!=c.Pipelines.Count||c.Facts.Any(f=>f.Key!="IncompleteExecutionContract")||
                catalog.Compositions.Count(x=>x.Identity==c.Identity||x.Key==c.Key||x.GeneratedNamespace==c.GeneratedNamespace)!=1)continue;
            var routes=new List<CanonicalMultiRouteEmission>();
            foreach(var route in c.Pipelines)
            {
                var graph=execution.Compositions[i].SingleOrDefault(g=>g.Contract.Anchor==route.Anchor);
                if(graph?.IsValid!=true)break;
                var projected=new CompositionSelectionCatalog(new(new[]{c with{Pipelines=new(new[]{route}),Facts=new(c.Facts.Where(f=>f.Anchor==route.Anchor))}}));
                var graphs=new CanonicalExecutionGraphCatalog(new(new[]{new EquatableArray<CanonicalExecutionGraphModel>(new[]{graph})}));
                var sync=CanonicalSyncEmissionNormalizer.Create(projected,graphs);
                var async=CanonicalAsyncEmissionNormalizer.Create(projected,graphs);
                var wrapper=CanonicalWrapperEmissionNormalizer.Create(projected,graphs);
                var capability=CanonicalCapabilityEmissionNormalizer.Create(projected,graphs,sync,async,wrapper);
                var s=sync.Compositions.SingleOrDefault();var a=async.Compositions.SingleOrDefault();var w=wrapper.Compositions.SingleOrDefault();var h=capability.Compositions.SingleOrDefault();
                if((s==null?0:1)+(a==null?0:1)+(w==null?0:1)+(h==null?0:1)!=1)break;
                var registration=s??(a==null?null:CanonicalAsyncEmissionNormalizer.Registration(a))??w?.Registration??h!.Registration;
                var key=c.Identity+"|"+route.Family+"|"+route.Request+"|"+route.Result;
                routes.Add(new(key,route.HandlerListOrdinal,route.PipelineListOrdinal,registration,s,a,w,h));
            }
            if(routes.Count==c.Pipelines.Count)result.Add(new(i,c.GeneratedNamespace,new(routes)));
        }
        return new(new(result));
    }
    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalMultiEmissionModel model,ImmutableArray<CompositionSelectionDiscovery.Binding> current,Compilation compilation,CancellationToken ct)=>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c=>c.Routes[0].Registration with{Index=c.Index}))),current,compilation,ct);
}
