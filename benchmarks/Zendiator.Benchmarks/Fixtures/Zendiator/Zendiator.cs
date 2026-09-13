using Zendiator;

namespace Zendiator.Benchmarks;

/// <summary>Composition root for benchmarks. One mediator per compilation;
/// pipeline depth is selected per request through marker interfaces.</summary>
[GenerateZendiator]
[PipelineBehavior(typeof(Zb1<,>), Order = 0)]
[PipelineBehavior(typeof(Zb2<,>), Order = 1)]
[PipelineBehavior(typeof(Zb3<,>), Order = 2)]
[PipelineBehavior(typeof(Zb4<,>), Order = 3)]
[PipelineBehavior(typeof(Zb5<,>), Order = 4)]
public sealed partial class Zendiator;
