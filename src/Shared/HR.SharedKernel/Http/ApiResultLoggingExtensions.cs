using Microsoft.Extensions.Logging;

namespace HR.SharedKernel.Http;

public static class ApiResultLoggingExtensions
{
    /// <summary>
    /// Logs operational failures (network, server, unreadable response) once, at the service boundary,
    /// with the operation name and failure kind only. Expected outcomes (401/403/404/validation/conflict)
    /// are not logged here. Request/response bodies, URLs, access tokens and user-entered text are never logged.
    /// </summary>
    public static ApiResult<T> LogFailure<T>(this ApiResult<T> result, ILogger? logger, string operation)
    {
        if (!result.Success
            && logger is not null
            && result.FailureKind is ApiFailureKind.Network or ApiFailureKind.Server or ApiFailureKind.InvalidResponse)
        {
            logger.LogWarning(
                "API call failed. Operation={Operation} FailureKind={FailureKind} Code={Code}",
                operation, result.FailureKind, result.Code);
        }

        return result;
    }
}
