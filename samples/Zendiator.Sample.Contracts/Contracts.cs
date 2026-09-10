namespace Zendiator.Sample.Contracts;

/// <summary>Marks the contracts assembly for compile-time discovery.</summary>
public sealed class ContractsAssemblyMarker;

/// <summary>Targets in a memorial year.</summary>
public sealed record MemorialTargetDto(int Year, string[] Names);

/// <summary>Queries memorial targets for a year. Struct to avoid allocation.</summary>
public readonly record struct GetTargetYearQuery(int Year) : IQuery<MemorialTargetDto>;

/// <summary>Household identifier returned on success.</summary>
public readonly record struct HouseholdId(Guid Value);

/// <summary>Validation failure details.</summary>
public sealed record ValidationError(string Field, string Message);

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

/// <summary>Registers a household. Returns a user-owned result instead of throwing.</summary>
public readonly record struct RegisterHouseholdCommand(string Name)
    : ICommand<Result<HouseholdId, ValidationError>>;

/// <summary>Purges a household without a response value.</summary>
public sealed record PurgeHouseholdCommand(Guid Id) : ICommand;

/// <summary>Counts households. Open over a marker type so callers keep their type arguments open.</summary>
public sealed record GetHouseholdCount<TMarker> : IRequest<int>;

/// <summary>Published after a household is purged.</summary>
public sealed record HouseholdPurged(Guid Id) : INotification;

/// <summary>A quote for household onboarding.</summary>
public sealed record HouseholdQuote(string Vendor, decimal Price);

/// <summary>Collects onboarding quotes from every vendor.</summary>
public sealed record GetHouseholdQuotes(string ProductCode) : IMultiRequest<HouseholdQuote>;

/// <summary>Parses a year from UTF-8 bytes without allocating.</summary>
public readonly ref struct ParseYear : ISyncRequest<int>
{
    public ParseYear(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

/// <summary>Resets cached state synchronously.</summary>
public readonly record struct ResetCache : ISyncCommand;

/// <summary>Legacy Unit-style request kept for compatibility.</summary>
public readonly record struct LegacyPing : ICommand;

/// <summary>Streams household names without materializing the whole list.</summary>
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;
