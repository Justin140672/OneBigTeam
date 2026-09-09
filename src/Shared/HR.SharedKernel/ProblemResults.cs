using Microsoft.AspNetCore.Http;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base code. Single translator from a failed <see cref="Result"/>
/// / <see cref="Error"/> to the corresponding HTTP <see cref="IResult"/>, so every opted-in
/// FastEndpoints endpoint maps <c>not_found</c> -&gt; 404 and <c>conflict</c>/<c>concurrency</c>
/// -&gt; 409 identically instead of repeating the branching in each Endpoint.cs.
///
/// The body shape is <c>{ error, code }</c>, matching what the existing endpoints already return so
/// the web client's error handling is unchanged.
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
