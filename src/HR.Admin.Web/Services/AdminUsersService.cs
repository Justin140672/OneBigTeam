using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the Platform Administrators endpoints (Admin User Management epic). Modeled exactly on
/// DeletionQueueService: HttpClientFactory "hrapi" client, GetXxxOrNullAsync returning null on any
/// failure (401/403/404 or a transport error), PostActionAsync-style methods returning bool or the
/// typed response (null on failure).
/// </summary>
public sealed class AdminUsersService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListPlatformAdministratorsResponse?> GetAdministratorsOrNullAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/platform-administrators", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ListPlatformAdministratorsResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the created administrator, or null on any failure (409 conflict for a duplicate
    /// email, 401 if the caller isn't an enabled PlatformOwner, or a transport error).
    /// </summary>
    public async Task<CreateAdministratorResponse?> CreateAdministratorAsync(
        string email, string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                "api/platform-administrators",
                new CreateAdministratorRequest(email, role),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CreateAdministratorResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<bool> PostActionAsync<TRequest>(
        string path, TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(path, request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<bool> DisableAdministratorAsync(Guid id, CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/platform-administrators/{id}/disable", new AdministratorIdRequest(id), cancellationToken);

    public Task<bool> EnableAdministratorAsync(Guid id, CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/platform-administrators/{id}/enable", new AdministratorIdRequest(id), cancellationToken);

    public Task<bool> AssignRoleAsync(Guid id, string role, CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/platform-administrators/{id}/role", new AssignAdministratorRoleRequest(id, role), cancellationToken);

    /// <summary>
    /// Performs a real MFA reset via the identity provider: removes every multi-factor factor for
    /// the administrator. Returns the typed response, or null on any failure (403/404/409/422/400
    /// or a transport error).
    /// </summary>
    public async Task<ResetAdministratorMfaResponse?> ResetMfaAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/platform-administrators/{id}/reset-mfa",
                new ResetAdministratorMfaRequest(id, true, reason),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ResetAdministratorMfaResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Fully implemented — sends a real Supabase password-recovery email.</summary>
    public Task<bool> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/platform-administrators/{id}/reset-password", new AdministratorIdRequest(id), cancellationToken);
}
