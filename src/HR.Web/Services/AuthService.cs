using System.Net.Http.Json;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

/// <summary>
/// Calls HR.Api's POST /api/login (HR.Modules.Identity's Login feature) — a real Supabase
/// password-grant sign-in for whatever email/password the user typed, replacing Login.razor's
/// earlier dev-persona-only stub. Works for both real self-service-signed-up accounts and seeded
/// Development personas (which have a real Supabase account too — see IdentityModule.
/// SeedDevSupabaseUsersAsync — just with SupabaseAuthGateway.DevSupabasePassword rather than a
/// literal "password").
/// </summary>
public sealed class AuthService(IHttpClientFactory httpClientFactory, ILogger<AuthService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<(LoginResult? Session, string? Error)> LoginAsync(string email, string password)
    {
        try
        {
            var response = await Http.PostAsJsonAsync("api/login", new { Email = email, Password = password });

            if (response.IsSuccessStatusCode)
            {
                var session = await response.Content.ReadFromJsonAsync<LoginResult>();
                return session is null ? (null, "Sign in failed. Please try again.") : (session, null);
            }

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return (null, body?.Error ?? "Invalid email or password.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log in {Email}", SensitiveDataScrubber.MaskEmail(email));
            return (null, "Something went wrong. Please try again.");
        }
    }

    public sealed record LoginResult(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed record ErrorResponse(string? Error);
}
