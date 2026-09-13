namespace Zendiator.Sample.Contracts;

/// <summary>Minimal user-owned result type. The library has no built-in result.</summary>
public readonly record struct Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(T value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    /// <summary>Gets the value on success.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result is failure.");

    /// <summary>Gets the error on failure.</summary>
    public TError Error => !IsSuccess ? _error! : throw new InvalidOperationException("Result is success.");

    /// <summary>Creates a success result.</summary>
    public static Result<T, TError> Success(T value) => new(value);

    /// <summary>Creates a failure result.</summary>
    public static Result<T, TError> Failure(TError error) => new(error);
}
