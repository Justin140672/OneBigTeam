using System.Net;
using System.Net.Http.Headers;

namespace HR.Web.Services;

// Replaces direct `IHttpClientFactory.CreateClient("hrapi")` + a pooled auth DelegatingHandler.
// Scoped, so it is resolved from the caller's OWN real DI scope (the circuit's scope for Blazor
// components/services, or the request's scope for minimal API endpoints) — never from
// IHttpClientFactory's internal handler-pipeline scope, which is what made the previous
// DelegatingHandler-based design a captive dependency (see CircuitSessionState remarks).
//
// The bearer token is attached here, at CreateClient() time, directly on the returned HttpClient's
// default headers, using CircuitSessionState resolved in the caller's real scope. This works
// correctly even for Blazor Server interactive circuit event handlers, which never have a live
// HttpContext: CircuitSessionState is a genuine per-circuit DI object, not an AsyncLocal value that
// would need to have flowed down from an unrelated earlier operation.
//
// Cross-tab session revocation (Ticket 9-13): HR.Api rejects a revoked (or expired) bearer token
// with 401 + "WWW-Authenticate: Bearer error=\"invalid_token\"". Previously nothing in HR.Web
// reacted to that on a LIVE circuit — the API rejected every call, but the circuit stayed on the
// page showing load errors until a full reload, so logging out in one tab never actually ejected
// the other tabs. Authenticated clients are therefore built over the SAME pooled "hrapi" handler
// pipeline wrapped in a small per-client (not pooled, not captive) handler that reports such a
// rejection back to this circuit's CircuitSessionState.
public sealed class HrApiHttpClientFactory(
    IHttpClientFactory httpClientFactory,
    CircuitSessionState sessionState,
    // Optional so lightweight test doubles can keep constructing this with just the two above; the
    // app's DI container always supplies it (AddHttpClient registers IHttpMessageHandlerFactory).
    IHttpMessageHandlerFactory? handlerFactory = null)
{
    public HttpClient CreateClient()
    {
        var accessToken = sessionState.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
            return httpClientFactory.CreateClient("hrapi");

        if (handlerFactory is null)
        {
            var plain = httpClientFactory.CreateClient("hrapi");
            plain.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return plain;
        }

        // Same configuration (BaseAddress/Timeout) and the same pooled handler pipeline as
        // CreateClient("hrapi"); only the outermost detector handler is per-client.
        using var configured = httpClientFactory.CreateClient("hrapi");
        var detector = new BearerRejectionDetectingHandler(sessionState, accessToken)
        {
            InnerHandler = handlerFactory.CreateHandler("hrapi"),
        };

        // disposeHandler: false — the inner handler is IHttpClientFactory's pooled pipeline, which
        // must never be disposed by a consumer (the outer detector holds no resources of its own).
        var client = new HttpClient(detector, disposeHandler: false)
        {
            BaseAddress = configured.BaseAddress,
            Timeout = configured.Timeout,
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private sealed class BearerRejectionDetectingHandler(CircuitSessionState sessionState, string accessToken)
        : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            // Only the JwtBearer challenge for an invalid token counts — an endpoint's own 401 (no
            // Bearer invalid_token challenge) is a business response, not a dead session.
            if (response.StatusCode == HttpStatusCode.Unauthorized && IsInvalidTokenChallenge(response))
                sessionState.ReportTokenRejected(accessToken);

            return response;
        }

        private static bool IsInvalidTokenChallenge(HttpResponseMessage response) =>
            response.Headers.WwwAuthenticate.Any(h =>
                string.Equals(h.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                (h.Parameter?.Contains("invalid_token", StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
