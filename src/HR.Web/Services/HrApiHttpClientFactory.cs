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
public sealed class HrApiHttpClientFactory(IHttpClientFactory httpClientFactory, CircuitSessionState sessionState)
{
    public HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("hrapi");

        var accessToken = sessionState.AccessToken;
        if (!string.IsNullOrEmpty(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }
}
