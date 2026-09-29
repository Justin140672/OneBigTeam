using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.SharedKernel;

namespace HR.Admin.Web.Services;

public sealed class AdminUsersService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

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

    public async Task<CreateAdministratorResponse?> CreateAdministratorAsync(
        string email, string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                "api/platform-administrators",
                new CreateAdministratorRequest(FormText.Required(email), FormText.Required(role)),
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
        PostActionAsync($"api/platform-administrators/{id}/role", new AssignAdministratorRoleRequest(id, FormText.Required(role)), cancellationToken);

    public async Task<ResetAdministratorMfaResponse?> ResetMfaAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/platform-administrators/{id}/reset-mfa",
                new ResetAdministratorMfaRequest(id, true, FormText.Required(reason)),
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

    public Task<bool> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/platform-administrators/{id}/reset-password", new AdministratorIdRequest(id), cancellationToken);
}
