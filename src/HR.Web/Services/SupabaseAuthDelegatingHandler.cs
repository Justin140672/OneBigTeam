using System.Net.Http.Headers;

namespace HR.Web.Services;

// Attaches "Authorization: Bearer {access_token}" to every outgoing "hrapi" request when a real
// Supabase session has been established (see SupabaseSessionAccessor). No-op when there is no
// stored access token — the existing dev-persona mechanism (DevAuthService/DevPersonaStore) keeps
// working unchanged in Development, since HR.Api's DevAuthHandler never inspects this header.
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
