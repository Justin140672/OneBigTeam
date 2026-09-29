using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class DepartureFinaliserApi
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    public static async Task<bool> FinalizeAsync(string apiBaseUrl, Guid employeeId)
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);
        var response = await http.PostAsync($"/api/dev/departure-finaliser/{AcmeId:N}/{employeeId:N}", content: null);
        return response.IsSuccessStatusCode;
    }

    private static async Task<HttpClient> CreateHrAdminClientAsync(string apiBaseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        HttpResponseMessage? sessionResponse = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{LauraUserId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }
        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{{userId}} to succeed, got {sessionResponse.StatusCode}.");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return http;
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);
}
