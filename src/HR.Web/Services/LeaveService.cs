using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class LeaveService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    // The API server serialises enums as strings; the default client options don't, so we need explicit options.
    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> CancelLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveRequestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LeaveRequestListResponse?> ListLeaveRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<LeaveRequestListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LeaveBalanceResponse?> GetEmployeeLeaveBalanceAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var year = DateTime.UtcNow.Year;
            return await Http.GetFromJsonAsync<LeaveBalanceResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(PreviewLeaveResponse? Response, string? Error)> PreviewLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        PreviewLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpResponse = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/preview",
                request, EnumOptions, cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
                return (await httpResponse.Content.ReadFromJsonAsync<PreviewLeaveResponse>(EnumOptions, cancellationToken), null);

            return (null, "Unable to calculate preview.");
        }
        catch (TaskCanceledException)
        {
            return (null, null);
        }
        catch
        {
            return (null, "Unable to calculate preview.");
        }
    }

    public async Task<(SubmitLeaveResponse? Response, string? Error)> SubmitLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        SubmitLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpResponse = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests",
                request, EnumOptions, cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
                return (await httpResponse.Content.ReadFromJsonAsync<SubmitLeaveResponse>(EnumOptions, cancellationToken), null);

            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var msg = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                return (null, msg ?? "Failed to submit leave request.");
            }
            catch
            {
                return (null, "Failed to submit leave request.");
            }
        }
        catch
        {
            return (null, "Failed to submit leave request.");
        }
    }
}
