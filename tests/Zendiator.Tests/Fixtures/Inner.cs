using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Inner<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "inner") where TRequest : IRequest<TResponse>;
