namespace Zendiator;

/// <summary>A synchronous request with one response type. May be a ref struct.</summary>
public interface ISyncRequest<TResponse>
{
}
/// <summary>A synchronous request without a response. May be a ref struct.</summary>
public interface ISyncRequest
{
}
