using HR.Web.Models;

namespace HR.Web.Services;

public sealed class CompensationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<CurrentCompensationModel?> GetCurrentCompensationAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<CurrentCompensationModel>(
                $"api/companies/{companyId}/employees/{employeeId}/compensation/current",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CompensationHistoryItemModel>> GetCompensationHistoryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GetCompensationHistoryResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/compensation/history",
                HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<(CreateCompensationRecordResponse? Result, string? Error)> CreateCompensationRecordAsync(
        Guid companyId, Guid employeeId, CreateCompensationRecordRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/compensation", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateCompensationRecordResponse>();
            return (created, null);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to create compensation record.");
        }

        return (null, "Failed to create compensation record.");
    }

    public async Task<(UpdateFutureCompensationRecordResponse? Result, string? Error)> UpdateFutureCompensationRecordAsync(
        Guid companyId, Guid employeeId, Guid id, UpdateFutureCompensationRecordRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/compensation/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateFutureCompensationRecordResponse>();
            return (updated, null);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to update compensation record.");
        }

        return (null, "Failed to update compensation record.");
    }

    public async Task<(bool Success, string? Error)> DeleteFutureCompensationRecordAsync(
        Guid companyId, Guid employeeId, Guid id)
    {
        var response = await Http.DeleteAsync(
            $"api/companies/{companyId}/employees/{employeeId}/compensation/{id}");

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "Failed to delete compensation record.");
        }

        return (false, "Failed to delete compensation record.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
