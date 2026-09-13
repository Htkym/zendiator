namespace Zendiator;

/// <summary>Handles a command with a response.</summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
/// <summary>Handles a command without a meaningful response (legacy Unit route).</summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit> where TCommand : ICommand;
