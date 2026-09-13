using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetLabelHandler : ISyncRequestHandler<GetLabel, string>
{
    public string Handle(GetLabel request, CancellationToken cancellationToken) => $"label-{request.Id}";
}
