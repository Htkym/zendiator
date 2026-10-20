using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamCursorFastPathEmitter
{
    internal static void Emit(StringBuilder b, string cursor, string sourceField)
    {
        b.AppendLine($$"""
                    private global::System.Threading.Tasks.ValueTask<bool> StartMove()
                    {
                        var invocation = new SynchronousMove(this);
                        var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.Create();
                        boundary.Start(ref invocation);
                        return invocation.Operation;
                    }
                    private struct SynchronousMove : global::System.Runtime.CompilerServices.IAsyncStateMachine
                    {
                        private readonly {{cursor}} _owner;
                        internal global::System.Threading.Tasks.ValueTask<bool> Operation;
                        internal SynchronousMove({{cursor}} owner) { _owner = owner; Operation = default; }
                        public void SetStateMachine(global::System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) { }
                        public void MoveNext()
                        {
                            try
                            {
                                while (true)
                                {
                                _owner._token.ThrowIfCancellationRequested();
                                var operation = _owner.{{sourceField}}!.MoveNextAsync();
                                if (operation.IsCompletedSuccessfully)
                                {
                                    if (operation.GetAwaiter().GetResult())
                                    {
                                        if (!_owner.TryPublishCurrent()) continue;
                                        Operation = new(true);
                                        global::System.Threading.Volatile.Write(ref _owner._busy, 0);
                                    }
                                    else Operation = _owner.FinishMove(new(false));
                                }
                                else Operation = _owner.FinishMove(operation);
                                return;
                                }
                            }
                            catch (global::System.Exception error)
                            {
                                Operation = _owner.FinishMove(global::System.Threading.Tasks.ValueTask.FromException<bool>(error));
                            }
                        }
                    }
                    private async global::System.Threading.Tasks.ValueTask<bool> FinishMove(global::System.Threading.Tasks.ValueTask<bool> operation)
                    {
                        try
                        {
                            while (true)
                            {
                                if (!await operation.ConfigureAwait(false))
                                { _ended = true; await Close().ConfigureAwait(false); return false; }
                                if (TryPublishCurrent()) return true;
                                _token.ThrowIfCancellationRequested();
                                operation = {{sourceField}}!.MoveNextAsync();
                            }
                        }
                        catch { _ended = true; await Close().ConfigureAwait(false); throw; }
                        finally { global::System.Threading.Volatile.Write(ref _busy, 0); }
                    }
            """);
    }
}
