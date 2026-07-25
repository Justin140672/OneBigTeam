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

    public async Task<(BulkApplyCompensationAdjustmentsResponse? Result, string? Error)> BulkApplyCompensationAdjustmentsAsync(
        Guid companyId, BulkApplyCompensationAdjustmentsRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/compensation/bulk", request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<BulkApplyCompensationAdjustmentsResponse>();
            return (result, null);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to apply bulk compensation adjustment.");
        }

        return (null, "Failed to apply bulk compensation adjustment.");
    }

    public async Task<(byte[]? Bytes, string? Error)> DownloadImportTemplateAsync(Guid companyId)
    {
        try
        {
            var response = await Http.GetAsync($"api/companies/{companyId}/compensation/import-template");

            if (!response.IsSuccessStatusCode)
                return (null, "Failed to download the compensation import template.");

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return (bytes, null);
        }
        catch
        {
            return (null, "Failed to download the compensation import template.");
        }
    }

    public async Task<(ImportCompensationChangesResponse? Result, string? Error, IReadOnlyList<CompensationImportRowError>? RowErrors)> ImportCompensationChangesAsync(
        Guid companyId, Stream fileStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(streamContent, "File", fileName);
        content.Add(new StringContent(companyId.ToString()), "CompanyId");

        var response = await Http.PostAsync($"api/companies/{companyId}/compensation/import", content);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ImportCompensationChangesResponse>();
            return (result, null, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var body = await response.Content.ReadFromJsonAsync<RowErrorsEnvelope>();
            return (null, "The file contains one or more errors.", body?.Errors ?? []);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to import compensation changes.", null);
        }

        return (null, "Failed to import compensation changes.", null);
    }

    private sealed record ErrorEnvelope(string? Error);
    private sealed record RowErrorsEnvelope(IReadOnlyList<CompensationImportRowError>? Errors);
}
