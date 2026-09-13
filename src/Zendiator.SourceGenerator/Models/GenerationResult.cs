using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class GenerationResult(GenerationModel? model, GenerationTarget? target, List<Diagnostic> diagnostics)
{
    public GenerationModel? Model { get; } = model;
    public GenerationTarget? Target { get; } = target;
    public List<Diagnostic> Diagnostics { get; } = diagnostics;
}
