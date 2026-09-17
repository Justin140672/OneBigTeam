using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class UserAdministrationService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListUsersResponse?> ListUsersAsync(
        Guid companyId, int page = 1, int pageSize = 100, string? search = null)
    {
        search = FormText.OptionalSearch(search);
        var url = $"api/companies/{companyId}/users?page={page}&pageSize={pageSize}";
        if (search is not null) url += $"&search={Uri.EscapeDataString(search)}";

        var result = await ApiResponseReader.ExecuteAsync<ListUsersResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    /// <summary>
    /// Fetches every page of the user list and returns the full set. The list endpoint is paged
    /// (server default/cap applies), so a single request silently drops anyone past the first page —
    /// this loops until the collected count reaches the reported TotalCount.
    /// </summary>
    public async Task<ListUsersResponse?> ListAllUsersAsync(Guid companyId, string? search = null)
    {
        const int pageSize = 500;
        var all = new List<UserListItemModel>();
        var page = 1;
        var totalCount = 0;

        while (true)
        {
            var result = await ListUsersAsync(companyId, page, pageSize, search);
            if (result is null)
                return page == 1 ? null : new ListUsersResponse(all, all.Count, 1, all.Count);

            totalCount = result.TotalCount;
            all.AddRange(result.Items);

            if (result.Items.Count == 0 || all.Count >= result.TotalCount)
                break;

            page++;
        }

        return new ListUsersResponse(all, Math.Max(totalCount, all.Count), 1, all.Count);
    }

    public async Task<List<InvitableEmployeeModel>?> GetInvitableEmployeesAsync(Guid companyId)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetInvitableEmployeesResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/users/invitable-employees", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value?.Items : null;
    }

    public async Task<GetUserDetailResponse?> GetUserAsync(Guid companyId, Guid employeeId)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetUserDetailResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/users/{employeeId}", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<GetUserAuditHistoryResponse?> GetAuditHistoryAsync(Guid companyId, Guid employeeId)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetUserAuditHistoryResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/users/{employeeId}/audit-history", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<(InviteEmployeeUserResponse? Result, string? Error)> InviteEmployeeUserAsync(
        Guid companyId, Guid employeeId, string email, List<Guid> roleIds)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/invite-user",
            new InviteEmployeeUserRequest(companyId, employeeId, email, roleIds));
        var result = await ApiResponseReader.ReadJsonAsync<InviteEmployeeUserResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to invite this employee."));
    }

    public async Task<(bool Success, string? Error)> UpdateUserRolesAsync(
        Guid companyId, Guid userId, List<Guid> roleIds)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/users/{userId}/roles",
            new UpdateUserRolesRequest(companyId, userId, roleIds));
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to update this user's roles."));
    }

    public async Task<(bool Success, string? Error)> ResendInviteAsync(Guid companyId, Guid inviteId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/invites/{inviteId}/resend", new { });
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to resend the invitation."));
    }

    public async Task<(bool Success, string? Error)> CancelInviteAsync(Guid companyId, Guid inviteId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/invites/{inviteId}/cancel", new { });
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to cancel the invitation."));
    }

    public async Task<(bool Success, string? Error)> DisableUserAsync(Guid companyId, Guid userId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/users/{userId}/disable", new { });
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to disable this account."));
    }

    public async Task<(bool Success, string? Error)> EnableUserAsync(Guid companyId, Guid userId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/users/{userId}/enable", new { });
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to enable this account."));
    }

    public async Task<List<EmployeeRoleOverrideModel>?> GetRoleOverridesAsync(Guid companyId, Guid userId)
    {
        var result = await ApiResponseReader.ExecuteAsync<ListEmployeeRoleOverridesResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/users/{userId}/role-overrides", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value?.Overrides : null;
    }

    public async Task<(AddEmployeeRoleOverrideResponse? Result, string? Error)> AddRoleOverrideAsync(
        Guid companyId, Guid userId, Guid roleId, EmployeeRoleOverrideType overrideType, string reason, DateTimeOffset? expiresAt)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/users/{userId}/role-overrides",
            new AddEmployeeRoleOverrideRequest(companyId, userId, roleId, overrideType, reason, expiresAt),
            HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<AddEmployeeRoleOverrideResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to add the permission override."));
    }

    public async Task<(bool Success, string? Error)> RemoveRoleOverrideAsync(Guid companyId, Guid userId, Guid roleId)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/users/{userId}/role-overrides/{roleId}");
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to remove the permission override."));
    }

    public async Task<(GetEffectiveAccessResponse? Result, string? Error)> GetEffectiveAccessAsync(Guid companyId, Guid employeeId)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetEffectiveAccessResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/users/{employeeId}/effective-access", ct), HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to load effective access for this user."));
    }
}
