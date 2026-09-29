using System.Net.Http.Headers;

namespace HR.Admin.Web.Services;

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
