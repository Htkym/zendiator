using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Parses a four-digit year from UTF-8 bytes on the calling stack.</summary>
public sealed class ParseYearHandler : ISyncRequestHandler<ParseYear, int>
{
    public int Handle(scoped ParseYear request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        return (data[0] - (byte)'0') * 1000 + (data[1] - (byte)'0') * 100 + (data[2] - (byte)'0') * 10 + (data[3] - (byte)'0');
    }
}
