using Microsoft.AspNetCore.Http;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base code, extended under Ticket 20 into the platform's single
/// canonical translator from a failed <see cref="Result"/> / <see cref="Error"/> to the corresponding
/// HTTP <see cref="IResult"/>. Every opted-in FastEndpoints endpoint should call
/// <c>await Send.ResultAsync(ProblemResults.FromError(result.Error));</c> inside its
/// <c>if (result.IsFailure)</c> branch instead of hand-rolling status-code switches, so the mapping
/// (and therefore the machine-readable <c>code</c> field relied on by the web client) can never drift
/// between endpoints. This is the same bug class as the Ticket 18 probation conflict incident, where
/// one endpoint hand-built its 409 response and silently dropped the error code.
///
/// Canonical mapping (do not change without updating every call site):
/// <list type="bullet">
/// <item><description><c>validation</c> business errors surfaced as <c>Error</c> values (not FluentValidation's
/// own FastEndpoints pipeline, which returns 400 automatically) -&gt; 400 via the default branch below.</description></item>
/// <item><description><c>not_found</c> -&gt; 404, with a body.</description></item>
/// <item><description><c>conflict</c> -&gt; 409, with a body.</description></item>
/// <item><description><c>concurrency</c> -&gt; 409, with a body. Same status as <c>conflict</c> but kept as a
/// distinct error code so the response body's <c>code</c> field lets the client tell an optimistic-concurrency
/// collision apart from an ordinary business conflict.</description></item>
/// <item><description><c>unauthorized</c> -&gt; 401, no body (matches ASP.NET Core's challenge convention).</description></item>
/// <item><description><c>forbidden</c> -&gt; 403, no body.</description></item>
/// <item><description>any other/unknown code -&gt; 400, with a body. Unrecognised codes are treated as ordinary
/// bad requests rather than failing closed as a 500, so a new Error.Code introduced by a handler degrades
/// gracefully instead of crashing the endpoint.</description></item>
/// </list>
///
/// The body shape, where a body is present, is always <c>{ error: message, code: errorCode }</c>, matching
/// what the existing endpoints already returned before adoption so the web client's error handling is
/// unchanged.
///
/// Documented exceptions that must NOT be routed through this translator (see
/// <c>HR.Architecture.Tests</c> for the enforced allow-list of endpoints permitted to keep manual mapping):
/// <list type="bullet">
/// <item><description>Information-hiding 404s, where an endpoint deliberately returns 404 instead of 403 to
/// avoid revealing that a resource exists to a caller who lacks access to it.</description></item>
/// <item><description>Authentication challenge endpoints that must return 401/403 before a domain
/// <see cref="Result"/> even exists (e.g. tenant/claim checks performed ahead of calling the handler).</description></item>
/// <item><description>File-download endpoints, which stream content or redirect rather than returning a JSON
/// problem body.</description></item>
/// <item><description>Endpoints relying on FastEndpoints' own built-in validation-failure response instead of
/// a domain <see cref="Result"/>.</description></item>
/// <item><description>Public/anonymous endpoints that deliberately return limited error detail (e.g. generic
/// messages to avoid enumeration or information disclosure).</description></item>
/// <item><description>Endpoints bound by an existing, separately-tested status-code contract that differs
/// from this mapping (e.g. <c>ValidateImportSession</c>, which returns 422 for business validation failures
/// per <c>DataImportHardeningEndpointTests</c>).</description></item>
/// </list>
/// Each exception site carries an inline "Ticket 20" comment explaining why it opts out.
/// </summary>
public static class ProblemResults
{
    public static IResult FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var body = new { error = error.Message, code = error.Code };

        return error.Code switch
        {
            "not_found" => TypedResults.NotFound(body),
            "conflict" or "concurrency" => TypedResults.Conflict(body),
            "unauthorized" => TypedResults.Unauthorized(),
            "forbidden" => TypedResults.Forbid(),
            _ => TypedResults.BadRequest(body),
        };
    }
}
