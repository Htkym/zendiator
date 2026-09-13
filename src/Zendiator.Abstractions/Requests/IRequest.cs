namespace Zendiator;

/// <summary>A request with one response type.</summary>
public interface IRequest<TResponse>;
/// <summary>A request without a meaningful response.</summary>
public interface IRequest : IRequest<Unit>;
