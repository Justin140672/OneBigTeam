using System.Net.Http.Headers;

namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.SupabaseAuthDelegatingHandler — attaches the Admin Portal's own session
// cookie's token as a Bearer token on every outgoing "hrapi" request.
public sealed class SupabaseAuthDelegatingHandler(SupabaseSessionAccessor sessionAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = sessionAccessor.AccessToken;
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
