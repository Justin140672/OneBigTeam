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

    public string? DisplayMessage =>
        ValidationErrors is { Count: > 0 }
            ? string.Join(" ", ValidationErrors.Values.SelectMany(m => m))
            : Error;
}
