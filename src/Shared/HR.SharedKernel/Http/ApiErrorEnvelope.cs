namespace HR.SharedKernel.Http;

/// <summary>
/// The single shared shape for API business-error responses: <c>{ "error": "...", "code": "..." }</c>.
/// "code" is optional; the well-known value "concurrency" flags an optimistic-concurrency 409.
/// Replaces the many local <c>ErrorEnvelope</c> record types previously duplicated per service file.
/// </summary>
public sealed record ApiErrorEnvelope(string? Error, string? Code = null);

/// <summary>
/// The single shared shape for API field-validation responses, matching FastEndpoints' own
/// <c>{ statusCode, message, errors: { field: [...] } }</c> validation-failure shape (only the
/// <c>errors</c> dictionary is needed by callers). Replaces local
/// <c>ValidationErrorResponse</c>/<c>ValidationErrorEnvelope</c> record types.
/// </summary>
public sealed record ApiValidationEnvelope(Dictionary<string, string[]>? Errors);
