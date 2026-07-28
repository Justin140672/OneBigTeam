using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class UserAdministrationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListUsersResponse?> ListUsersAsync(
        Guid companyId, int page = 1, int pageSize = 100, string? search = null)
    {
        try
        {
            var url = $"api/companies/{companyId}/users?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

            return await Http.GetFromJsonAsync<ListUsersResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetUserDetailResponse?> GetUserAsync(Guid companyId, Guid employeeId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetUserDetailResponse>(
                $"api/companies/{companyId}/users/{employeeId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetUserAuditHistoryResponse?> GetAuditHistoryAsync(Guid companyId, Guid employeeId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetUserAuditHistoryResponse>(
                $"api/companies/{companyId}/users/{employeeId}/audit-history", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(InviteEmployeeUserResponse? Result, string? Error)> InviteEmployeeUserAsync(
        Guid companyId, Guid employeeId, string email, List<Guid> roleIds)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/invite-user",
            new InviteEmployeeUserRequest(companyId, employeeId, email, roleIds));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<InviteEmployeeUserResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to invite this employee."));
    }

    public async Task<(bool Success, string? Error)> UpdateUserRolesAsync(
        Guid companyId, Guid userId, List<Guid> roleIds)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/users/{userId}/roles",
            new UpdateUserRolesRequest(companyId, userId, roleIds));

        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ReadErrorAsync(response, "Failed to update this user's roles."));
    }

    public async Task<(bool Success, string? Error)> ResendInviteAsync(Guid companyId, Guid inviteId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/invites/{inviteId}/resend", new { });

        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ReadErrorAsync(response, "Failed to resend the invitation."));
    }

    public async Task<(bool Success, string? Error)> CancelInviteAsync(Guid companyId, Guid inviteId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/invites/{inviteId}/cancel", new { });

        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ReadErrorAsync(response, "Failed to cancel the invitation."));
    }

    public async Task<(bool Success, string? Error)> DisableUserAsync(Guid companyId, Guid userId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/users/{userId}/disable", new { });

        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ReadErrorAsync(response, "Failed to disable this account."));
    }

    public async Task<(bool Success, string? Error)> EnableUserAsync(Guid companyId, Guid userId)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/users/{userId}/enable", new { });

        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ReadErrorAsync(response, "Failed to enable this account."));
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return body?.Error ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record ErrorEnvelope(string? Error);
}
