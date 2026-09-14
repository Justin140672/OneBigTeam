using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Ticket 3 (P1) follow-up: on a timeout or connection failure, <c>Outcome.Result</c> in the
/// standard resilience handler's retry predicate is null - there is no HTTP response to read the
/// original request's method off. Attach the method to the operation's <see cref="ResilienceContext"/>
/// before the resilience handler runs, so the predicate can always recover it regardless of outcome.
///
/// Registered via <c>http.AddHttpMessageHandler</c> BEFORE <c>AddStandardResilienceHandler</c>, so it
/// wraps the resilience handler and runs once per logical call (not once per retry attempt) - a
/// <see cref="ResilienceContext"/> attached to the request via <c>SetResilienceContext</c> is reused
/// by the resilience handler across every attempt of that same call, rather than it creating its own.
/// </summary>
internal sealed class RequestMethodCapturingHandler : DelegatingHandler
{
    public static readonly ResiliencePropertyKey<HttpMethod> RequestMethodKey = new("Ticket3.RequestMethod");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = request.GetResilienceContext();
        var ownsContext = context is null;
        context ??= ResilienceContextPool.Shared.Get(cancellationToken);

        if (ownsContext)
            request.SetResilienceContext(context);

        context.Properties.Set(RequestMethodKey, request.Method);

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            // Only return a context this handler itself rented from the pool - one already attached
            // by an earlier caller belongs to that caller, not to us.
            if (ownsContext)
                ResilienceContextPool.Shared.Return(context);
        }
    }
}
