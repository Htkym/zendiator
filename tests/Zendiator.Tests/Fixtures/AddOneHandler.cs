using global::Zendiator;

namespace Zendiator.Tests;

public sealed class AddOneHandler : ISyncRequestHandler<AddOne, int>
{
    public static int Calls;
    public Guid Id { get; } = Guid.NewGuid();
    public int Handle(AddOne request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return request.Value + 1;
    }
}
