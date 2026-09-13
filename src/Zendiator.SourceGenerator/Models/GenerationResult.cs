using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class GenerationResult(string source, string interceptors, List<Diagnostic> diagnostics)
{
    public string Source { get; } = source;
    public string Interceptors { get; } = interceptors;
    public List<Diagnostic> Diagnostics { get; } = diagnostics;
}
