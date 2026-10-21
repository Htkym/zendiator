namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamPrototypeEmitter
{
    internal static CanonicalSourceFile Emit(CanonicalStreamPrototypeModel model) =>
        CanonicalStreamFallbackEmitter.EmitIterator(model.Namespace, model.ClassName, model.HintName,
            model.Handler, model.Hooks, model.Generic, null);
}
