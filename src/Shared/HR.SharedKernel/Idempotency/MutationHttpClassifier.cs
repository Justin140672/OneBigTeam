using System.Net;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) final follow-up: the single place that maps an HTTP outcome to a
/// <see cref="MutationOutcomeKind"/>, so every idempotent-mutation service method (LeaveService,
/// AssetService, ...) classifies consistently instead of re-deriving this table inline. See
/// <see cref="MutationOutcomeKind"/> for what each classification means for key retention.
///
/// Classification table (Ticket 3 final gap spec):
/// - 2xx with a parseable response             -&gt; Succeeded
/// - 400/422 validation failure                -&gt; Rejected
/// - 401/403 authorization failure              -&gt; Rejected
/// - 404 not found                              -&gt; Rejected
/// - 409 idempotency/business conflict          -&gt; Rejected
/// - 408 request timeout                        -&gt; AmbiguousFailure
/// - 429 too many requests                      -&gt; AmbiguousFailure
/// - 500-599 server error                       -&gt; AmbiguousFailure
/// - any other/unrecognised status              -&gt; AmbiguousFailure (fail safe: never discard a key
///   on a status this table doesn't explicitly know to be a definitive rejection)
/// </summary>
public static class MutationHttpClassifier
{
    public static MutationOutcomeKind ClassifyStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;

        if (code is >= 200 and < 300)
            return MutationOutcomeKind.Succeeded;

        return statusCode switch
        {
            HttpStatusCode.BadRequest => MutationOutcomeKind.Rejected,
            HttpStatusCode.UnprocessableEntity => MutationOutcomeKind.Rejected,
            HttpStatusCode.Unauthorized => MutationOutcomeKind.Rejected,
            HttpStatusCode.Forbidden => MutationOutcomeKind.Rejected,
            HttpStatusCode.NotFound => MutationOutcomeKind.Rejected,
            HttpStatusCode.Conflict => MutationOutcomeKind.Rejected,
            HttpStatusCode.RequestTimeout => MutationOutcomeKind.AmbiguousFailure,
            HttpStatusCode.TooManyRequests => MutationOutcomeKind.AmbiguousFailure,
            _ when code is >= 500 and < 600 => MutationOutcomeKind.AmbiguousFailure,
            _ => MutationOutcomeKind.AmbiguousFailure,
        };
    }
}
