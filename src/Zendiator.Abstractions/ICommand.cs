namespace Zendiator;

/// <summary>A command with a response.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;
/// <summary>A command without a meaningful response.</summary>
public interface ICommand : IRequest, ICommand<Unit>;
