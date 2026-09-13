using global::Zendiator;

namespace Zendiator.Tests;

public sealed class DeleteUserHandler(Trace trace) : IRequestHandler<DeleteUser>
{
    public static List<int> Deleted { get; } = [];

    public async ValueTask HandleAsync(DeleteUser command, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        trace.Add($"deleted:{command.UserId}");
        Deleted.Add(command.UserId);
    }
}
