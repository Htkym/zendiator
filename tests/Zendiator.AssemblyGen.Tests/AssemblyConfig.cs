using Zendiator;
using Zendiator.AssemblyGen.Tests;

[assembly: GenerateZendiator(Namespace = "AsmGen.Generated")]
[assembly: IncludeAssembly(typeof(AssemblyMarker))]
[assembly: PipelineBehavior(typeof(TracingBehavior<,>), Order = 0)]
[assembly: PipelineBehavior(typeof(AsmStreamTrace), Order = 1)]
