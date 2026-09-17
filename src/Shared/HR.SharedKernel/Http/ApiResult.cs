namespace HR.SharedKernel.Http;

/// <summary>
/// Classifies why an HTTP call to the API did not produce a usable value. Callers use this to
/// distinguish "there is no data" from "the API call failed" instead of collapsing both into
/// null/false/an empty collection.
/// </summary>
public enum ApiFailureKind
{
    /// <summary>The call succeeded (Success is true).</summary>
    None = 0,

    /// <summary>401 — the caller is not authenticated.</summary>
    Unauthenticated,

    /// <summary>403 — the caller is authenticated but not permitted.</summary>
    Forbidden,

    /// <summary>404 — the resource does not exist. Only treat this as "no data" where a 404 is an
    /// explicitly expected, legitimate outcome for the call; otherwise surface it as a failure.</summary>
    NotFound,

    /// <summary>400/422 — field-level validation failed.</summary>
    Validation,

    /// <summary>409 business conflict (code is not "concurrency") — e.g. a duplicate email.</summary>
    Conflict,

    /// <summary>409 optimistic-concurrency conflict (code == "concurrency") — the record changed
    /// since it was loaded. Drives <c>SaveConflictBanner</c>.</summary>
    Concurrency,

    /// <summary>The request never reached/returned from the API — DNS/connection/timeout failure.</summary>
    Network,

    /// <summary>5xx or another unmapped non-success status code.</summary>
    Server,

    /// <summary>The response body could not be parsed into the expected shape.</summary>
    InvalidResponse
}

/// <summary>
/// Shared, typed outcome of an HTTP call to the API. Replaces the many bespoke
/// null/false/empty-collection return shapes previously used across HR.Web and HR.Admin.Web
/// service classes, which could not distinguish "no data" from "the API call failed".
/// </summary>
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

    /// <summary>True when this failure is an optimistic-concurrency conflict (code == "concurrency"),
    /// the convention already used by <c>EditSectionBase</c>/<c>SaveConflictBanner</c>.</summary>
    public bool IsConcurrencyConflict => FailureKind == ApiFailureKind.Concurrency;

    /// <summary>Flattens ValidationErrors into a single display string, falling back to Error.</summary>
    public string? DisplayMessage =>
        ValidationErrors is { Count: > 0 }
            ? string.Join(" ", ValidationErrors.Values.SelectMany(m => m))
            : Error;
}
