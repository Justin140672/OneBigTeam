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
