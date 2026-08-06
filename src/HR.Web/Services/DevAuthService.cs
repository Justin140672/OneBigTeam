using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

public sealed record DevPersonaDto(string UserId, string Name, string JobTitle, string Email);

public sealed record DevSupabaseSessionDto(string AccessToken, string RefreshToken, int ExpiresIn);

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

    // Calls HR.Api's dev persona-switch endpoint, which performs a real Supabase password-grant
    // login for that persona (see the "Switch development to real Supabase auth" plan) and returns
    // the resulting tokens. Establishing the session cookie from those tokens must happen via a real
    // browser navigation to HR.Web's /dev/persona-cookie endpoint (see MainLayout.razor's
    // OnPersonaSwitchAsync) — not a server-side HTTP call, since Set-Cookie on a request made from
    // inside the Blazor Server process never reaches the user's actual browser cookie jar.
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
