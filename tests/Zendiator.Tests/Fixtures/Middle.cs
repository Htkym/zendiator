using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Middle<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "middle") where TRequest : IRequest<TResponse>;
