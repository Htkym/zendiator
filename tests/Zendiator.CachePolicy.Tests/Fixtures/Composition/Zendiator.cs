using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

// One mediator per compilation; all behaviors below are closed.

[GenerateZendiator]
[PipelineBehavior(typeof(PipeB0), Order = 0)]
[PipelineBehavior(typeof(PipeB1), Order = 1)]
[PipelineBehavior(typeof(PipeB2), Order = 2)]
[PipelineBehavior(typeof(GateBehavior), Order = 3)]
[PipelineBehavior(typeof(RetryTwiceBehavior), Order = 4)]
[PipelineBehavior(typeof(TokBehavior), Order = 5)]
[PipelineBehavior(typeof(AddTenBehavior), Order = 6)]
[PipelineBehavior(typeof(NullPassBehavior), Order = 7)]
[PipelineBehavior(typeof(ExpPipeBehavior), Order = 8)]
[PipelineBehavior(typeof(SuspBehavior), Order = 9)]
[PipelineBehavior(typeof(FailFinallyBehavior), Order = 10)]
[PipelineBehavior(typeof(ReenterBehavior), Order = 11)]
public sealed partial class Zendiator;
