using System.Net.Http.Headers;

namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.HrApiHttpClientFactory — see that file's remarks. Replaces the previous
// SupabaseAuthDelegatingHandler, which was resolved via IHttpClientFactory's own internal,
// HandlerLifetime-scoped container rather than the calling request/circuit's scope (a captive
// dependency, and the root cause of the P1 finding that the FIRST admin session captured this way
// would silently be reused for every subsequent, possibly different, platform administrator for as
// long as the pooled handler lived). Attaching the token here, at CreateClient() time, executes in
// the caller's own real DI scope, so it always reflects the correct circuit/request's identity.
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
