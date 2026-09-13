namespace Zendiator;

/// <summary>A request with one response type delivered to multiple handlers.</summary>
public interface IMultiRequest<TResponse> : IRequest<TResponse>
{
}
/// <summary>A request without a response delivered to multiple handlers.</summary>
public interface IMultiRequest : IRequest
{
}
