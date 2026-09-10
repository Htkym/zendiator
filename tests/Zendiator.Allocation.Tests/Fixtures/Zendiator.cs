using Zendiator;

namespace Zendiator.Allocation.Tests;

[GenerateZendiator]
[PipelineBehavior(typeof(PassThrough), Order = 0)]
public sealed partial class Zendiator;
