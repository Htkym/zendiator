using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Outer<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "outer") where TRequest : IRequest<TResponse>;
