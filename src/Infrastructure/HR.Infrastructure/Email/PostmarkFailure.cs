using System.Net;
using System.Text.Json;

namespace HR.Infrastructure.Email;

/// <summary>
/// Bounded, personal-data-free classification of a failed Postmark API call. Only these values
/// (plus the HTTP status and Postmark's numeric ErrorCode) are ever logged or placed in exception
/// text — never Postmark's free-form <c>Message</c> (it can echo the rejected recipient address,
/// e.g. "Inactive recipient user@example.com") and never the caller-controlled subject.
/// </summary>
internal enum PostmarkFailureCategory
{
    Other,
    Authentication,
    InvalidRequest,
    SenderSignature,
    InactiveRecipient,
    AccountRestricted,
    Template,
    RateLimited,
    ProviderUnavailable,
}

internal readonly record struct PostmarkFailure(int StatusCode, int? ErrorCode, PostmarkFailureCategory Category)
{
    /// <summary>
    /// Reads only the numeric <c>ErrorCode</c> from the response body. The body itself, and its
    /// <c>Message</c> field, are deliberately discarded.
    /// </summary>
    public static async Task<PostmarkFailure> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var errorCode = await ReadErrorCodeAsync(response, ct);
        return new PostmarkFailure(status, errorCode, Classify(response.StatusCode, errorCode));
    }

    public static PostmarkFailureCategory Classify(HttpStatusCode status, int? errorCode)
    {
        var byCode = errorCode switch
        {
            10 => PostmarkFailureCategory.Authentication,
            300 or 402 or 403 or 409 or 410 or 411 => PostmarkFailureCategory.InvalidRequest,
            400 or 401 => PostmarkFailureCategory.SenderSignature,
            406 => PostmarkFailureCategory.InactiveRecipient,
            405 or 412 => PostmarkFailureCategory.AccountRestricted,
            >= 1100 and < 1200 => PostmarkFailureCategory.Template,
            _ => (PostmarkFailureCategory?)null,
        };
        if (byCode is { } category)
            return category;

        var code = (int)status;
        return code switch
        {
            401 or 403 => PostmarkFailureCategory.Authentication,
            429 => PostmarkFailureCategory.RateLimited,
            >= 500 => PostmarkFailureCategory.ProviderUnavailable,
            _ => PostmarkFailureCategory.Other,
        };
    }

    public string ToExceptionMessage(string operation) =>
        $"{operation} failed. StatusCode={StatusCode} PostmarkErrorCode={ErrorCode?.ToString() ?? "none"} FailureCategory={Category}";

    private static async Task<int?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("ErrorCode", out var c)
                   && c.ValueKind == JsonValueKind.Number
                   && c.TryGetInt32(out var code)
                ? code
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
