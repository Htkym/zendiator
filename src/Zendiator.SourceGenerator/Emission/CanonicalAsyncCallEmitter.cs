using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalAsyncCallEmitter
{
    internal const string PoolingBuilderAttribute = "[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]";

    internal static void EmitCall(StringBuilder b, string name, string result, string receiver,
        string contract, string method, BoundAsyncCallTargetEmission? target, string[] parameters, string[] names)
    {
        var fallback = "((" + contract + ")receiver)." + method + "(" + string.Join(", ", names) + ")";
        var call = fallback;
        if (target != null)
        {
            var arguments = string.Join(", ", names.Select((n, i) => "(" + target.ParameterTypes[i] + ")" + n));
            var direct = "((" + target.ImplementationTypeName + ")receiver)." + target.MethodName + "(" + arguments + ")";
            call = target.RequiresExactReceiver ? "((global::System.Object)receiver).GetType() == typeof(" + target.ReceiverName + ") ? " + direct + " : " + fallback : direct;
        }
        b.AppendLine("        private static " + result + " " + name + "(" + receiver + " receiver, " +
            string.Join(", ", parameters.Select((p, i) => p + " " + names[i])) + ") => " + call + ";");
    }
}
