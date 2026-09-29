using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace HR.Web.Services;

public sealed class PasswordResetService(HrApiHttpClientFactory httpClientFactory, ILogger<PasswordResetService> logger)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<bool> RequestResetAsync(string email, string? userAgent = null)
    {
        try
        {
            var response = await Http.PostAsJsonAsync("api/forgot-password", new { Email = email, UserAgent = userAgent });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // No safe identifier is available here (the endpoint never reveals whether the email
            // matched an account) — deliberately does not log the email address.
            logger.LogWarning(ex, "Failed to request password reset.");
            return false;
        }
    }

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
