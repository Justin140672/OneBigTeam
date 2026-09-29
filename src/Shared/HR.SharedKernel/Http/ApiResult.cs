namespace HR.SharedKernel.Http;

public enum ApiFailureKind
{
    None = 0,

    Unauthenticated,

    Forbidden,

    NotFound,

    Validation,

    Conflict,

    Concurrency,

    Network,

    Server,

    InvalidResponse
}

public sealed record ApiResult<T>(
    bool Success,
    T? Value,
    ApiFailureKind FailureKind,
    string? Error,
    string? Code,
    IReadOnlyDictionary<string, string[]>? ValidationErrors)
{
    public static ApiResult<T> Ok(T? value) => new(true, value, ApiFailureKind.None, null, null, null);

    public static ApiResult<T> Fail(
        ApiFailureKind kind,
        string? error,
        string? code = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
        => new(false, default, kind, error, code, validationErrors);

    public bool IsConcurrencyConflict => FailureKind == ApiFailureKind.Concurrency;

    /// <summary>
    /// True when repeating the same request could plausibly succeed (transient network, server or
    /// unreadable-response failures). Authentication, permission, validation, not-found and conflict
    /// failures are not retryable without the user changing something first.
    /// </summary>
    public bool IsRetryable =>
        FailureKind is ApiFailureKind.Network or ApiFailureKind.Server or ApiFailureKind.InvalidResponse;

    /// <summary>Projects a successful value while carrying a failure through unchanged.</summary>
    public ApiResult<TOut> Map<TOut>(Func<T?, TOut?> selector)
        => Success
            ? ApiResult<TOut>.Ok(selector(Value))
            : ApiResult<TOut>.Fail(FailureKind, Error, Code, ValidationErrors);

    public string? DisplayMessage =>
        ValidationErrors is { Count: > 0 }
            ? string.Join(" ", ValidationErrors.Values.SelectMany(m => m))
            : Error;
}
