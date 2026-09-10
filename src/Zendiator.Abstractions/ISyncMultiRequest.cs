namespace Zendiator;

/// <summary>A synchronous multi-handler request with one response type. May be a ref struct.</summary>
public interface ISyncMultiRequest<TResponse> : ISyncRequest<TResponse>
{
}
/// <summary>A synchronous multi-handler request without a response. May be a ref struct.</summary>
public interface ISyncMultiRequest : ISyncRequest
{
}
