using System.Net.Http.Json;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

/// <summary>
/// Calls HR.Api's POST /api/forgot-password (HR.Modules.Identity's RequestPasswordReset feature),
/// which sends a real Supabase password-recovery email to a matching account. Always reports
/// success from the caller's point of view — the endpoint itself never reveals whether the email
/// matched an account (see RequestPasswordResetHandler's own comment) — so this only surfaces a
/// failure for a genuine request error (network/5xx), not "email not found".
/// </summary>
public sealed class PasswordResetService(IHttpClientFactory httpClientFactory, ILogger<PasswordResetService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<bool> RequestResetAsync(string email, string? userAgent = null)
    {
        try
        {
            var response = await Http.PostAsJsonAsync("api/forgot-password", new { Email = email, UserAgent = userAgent });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to request password reset for {Email}", SensitiveDataScrubber.MaskEmail(email));
            return false;
        }
    }

    /// <summary>
    /// Submits a new password using the short-lived access token from a password-recovery
    /// redirect (see ResetPasswordComplete.razor). Unlike RequestResetAsync, a failure here IS
    /// meaningful and surfaced to the caller — most likely the recovery link has expired or was
    /// already used (see ResetPasswordHandler's own comment).
    /// </summary>
    public async Task<(bool Success, string? Error)> ResetPasswordAsync(string accessToken, string newPassword)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                "api/reset-password", new { AccessToken = accessToken, NewPassword = newPassword });

            if (response.IsSuccessStatusCode)
                return (true, null);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return (false, body?.Error ?? "This link is invalid or has expired.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reset password");
            return (false, "Something went wrong. Please try again.");
        }
    }

    private sealed record ErrorResponse(string? Error);
}
