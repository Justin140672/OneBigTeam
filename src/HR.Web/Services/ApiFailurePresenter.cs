using HR.SharedKernel.Http;

namespace HR.Web.Services;

/// <summary>What the UI should tell the user about a failed API call, and whether offering "Try again" is sensible.</summary>
public sealed record ApiFailurePresentation(string Message, bool IsRetryable);

/// <summary>
/// Maps a failed <see cref="ApiResult{T}"/> to a safe, user-facing message. Raw exception text, stack
/// traces, response bodies and status-code dumps are never surfaced: server (5xx), network and
/// malformed-response failures always get a fixed message; only API-authored validation and conflict
/// messages (which are written for end users) are passed through.
/// </summary>
public static class ApiFailurePresenter
{
    /// <param name="result">The failed result.</param>
    /// <param name="action">Short verb phrase used in messages, e.g. "upload your photo" or "load notifications".</param>
    public static ApiFailurePresentation Present<T>(ApiResult<T> result, string action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var message = result.FailureKind switch
        {
            ApiFailureKind.Unauthenticated => "Your session has expired. Please sign in again.",
            ApiFailureKind.Forbidden => $"You do not have permission to {action}.",
            ApiFailureKind.NotFound => $"We couldn't {action} because the item no longer exists.",
            ApiFailureKind.Validation => NonEmpty(result.DisplayMessage) ?? "Some of the information provided is not valid. Please check it and try again.",
            ApiFailureKind.Conflict => NonEmpty(result.Error) ?? $"We couldn't {action} because it conflicts with the current state.",
            ApiFailureKind.Concurrency => "This record was changed by someone else. Reload it to see the latest version, then try again.",
            ApiFailureKind.Network => $"We couldn't reach the server to {action}. Check your connection and try again.",
            ApiFailureKind.InvalidResponse => $"We received an unexpected response while trying to {action}. Please try again.",
            _ => $"Something went wrong while trying to {action}. Please try again.",
        };

        return new ApiFailurePresentation(message, result.IsRetryable);
    }

    /// <summary>Just the safe message text for a failed result.</summary>
    public static string Message<T>(ApiResult<T> result, string action) => Present(result, action).Message;

    /// <summary>Null when the call succeeded, otherwise the safe message. Adapts to "string? error" form contracts.</summary>
    public static string? ErrorOrNull<T>(ApiResult<T> result, string action) =>
        result.Success ? null : Message(result, action);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
