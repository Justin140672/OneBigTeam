using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace HR.Admin.Web.Services;

public sealed record DevPersonaDto(string UserId, string Name, string JobTitle, string Email);

public sealed record DevSupabaseSessionDto(string AccessToken, string RefreshToken, int ExpiresIn);

// Mirrors HR.Web.Services.DevAuthService — reuses the same HR.Api dev-persona-switch endpoints
// (/api/dev/personas, /api/dev/persona/{userId}), which perform a real Supabase password-grant
// login. Development-only; production Admin Portal sign-in is out of scope for this story (real
// Supabase email/password sign-in, matching HR.Web's Login.razor flow, is a follow-up item).
public sealed class DevAuthService(IHttpClientFactory httpClientFactory, ILogger<DevAuthService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<IReadOnlyList<DevPersonaDto>> GetPersonasAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<IReadOnlyList<DevPersonaDto>>("api/dev/personas") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch dev personas from HR.Api");
            return [];
        }
    }

    /// <summary>
    /// Probes GET /api/platform-admin/me with an explicit bearer token (the freshly minted session,
    /// before its cookie hop) to decide whether this account is actually authorised for the Admin
    /// Portal. Lets Login.razor reject a valid-but-not-platform-admin sign-in on the login page
    /// itself rather than letting them briefly land on a dashboard they can't use.
    /// </summary>
    public async Task<bool> IsPlatformAdminAsync(string accessToken)
    {
        try
        {
            // Deliberately NOT the "hrapi" client: its SupabaseAuthDelegatingHandler is pooled by
            // IHttpClientFactory and can carry a *previous* session's captured token (the classic
            // pooled-handler-with-scoped-dependency trap), which would overwrite the explicit
            // bearer below and make this probe answer for the wrong user. A bare client with just
            // the base address and the token we were handed is unambiguous.
            using var http = new HttpClient { BaseAddress = Http.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/platform-admin/me");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check platform-admin authorisation");
            return false;
        }
    }

    public async Task<DevSupabaseSessionDto?> SwitchAsync(string userId)
    {
        try
        {
            var response = await Http.PostAsync($"api/dev/persona/{userId}", null);
            if (!response.IsSuccessStatusCode)
                return null;

            var session = await response.Content.ReadFromJsonAsync<DevSupabaseSessionDto>();
            return string.IsNullOrWhiteSpace(session?.AccessToken) ? null : session;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to switch dev persona {UserId}", userId);
            return null;
        }
    }
}
