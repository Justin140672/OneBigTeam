using System.Diagnostics.CodeAnalysis;

namespace HR.SharedKernel;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Successful result cannot have an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Failed result must include an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    /// <summary>
    /// Shadows <see cref="Result.IsSuccess"/> purely to attach a nullability contract: callers that
    /// check <c>IsSuccess</c> before reading <see cref="Value"/> (the standard pattern throughout
    /// this codebase, e.g. in handler tests) no longer trigger CS8602 "possibly null reference" on
    /// <c>Value</c> — the compiler now understands the invariant already enforced at runtime by the
    /// base constructor (a successful Result always carries a non-null value via <see cref="Success"/>).
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public new bool IsSuccess => base.IsSuccess;

    public static Result<T> Success(T value) => new(value, true, Error.None);

    public static new Result<T> Failure(Error error) => new(default, false, error);
}
