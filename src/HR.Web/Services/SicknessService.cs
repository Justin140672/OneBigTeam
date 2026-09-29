using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

/// <summary>
/// Sickness API client. Reads and writes return <see cref="ApiResult{T}"/> so callers can distinguish an
/// empty result from 401/403/404/409/422/5xx/network/malformed-response failures; mutations never report
/// success when the API call failed and caller cancellation propagates.
/// </summary>
public sealed class SicknessService(
    HrApiHttpClientFactory httpClientFactory,
    ILogger<SicknessService>? logger = null)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    private async Task<ApiResult<T>> GetAsync<T>(string operation, string url, CancellationToken cancellationToken)
    {
        var result = await ApiResponseReader.ExecuteAsync<T>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default, cancellationToken);
        return result.LogFailure(logger, operation);
    }

    private async Task<ApiResult<Unit>> PostNoContentAsync(
        string operation, string url, object body, CancellationToken cancellationToken)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PostAsJsonAsync(url, body, HrApiJsonOptions.Default, ct), cancellationToken);
        return result.LogFailure(logger, operation);
    }

    public Task<ApiResult<ReturnToWorkReviewDetailModel>> GetReturnToWorkReviewAsync(
        Guid companyId,
        Guid reviewId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ReturnToWorkReviewDetailModel>(
            "Sickness.GetReturnToWorkReview",
            $"api/companies/{companyId}/return-to-work-reviews/{reviewId}", cancellationToken);

    public Task<ApiResult<ListEmployeeSicknessRecordsResponseModel>> ListEmployeeSicknessRecordsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ListEmployeeSicknessRecordsResponseModel>(
            "Sickness.ListEmployeeRecords",
            $"api/companies/{companyId}/employees/{employeeId}/sickness-records", cancellationToken);

    public Task<ApiResult<ListEmployeeSicknessRecordsResponseModel>> GetMySicknessRecordsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ListEmployeeSicknessRecordsResponseModel>(
            "Sickness.GetMyRecords",
            $"api/companies/{companyId}/employees/{employeeId}/sickness-records/my", cancellationToken);

    public Task<ApiResult<Unit>> RecordSicknessAsync(
        Guid companyId,
        Guid employeeId,
        RecordSicknessRequest request,
        CancellationToken cancellationToken = default) =>
        PostNoContentAsync(
            "Sickness.Record",
            $"api/companies/{companyId}/employees/{employeeId}/sickness-records",
            request, cancellationToken);

    public Task<ApiResult<Unit>> RecordMySicknessAsync(
        Guid companyId,
        Guid employeeId,
        RecordSicknessRequest request,
        CancellationToken cancellationToken = default) =>
        PostNoContentAsync(
            "Sickness.RecordMine",
            $"api/companies/{companyId}/employees/{employeeId}/sickness-records/my",
            request, cancellationToken);

    public Task<ApiResult<Unit>> CloseSicknessRecordAsync(
        Guid companyId,
        Guid employeeId,
        Guid recordId,
        CloseSicknessRecordRequest request,
        CancellationToken cancellationToken = default) =>
        PostNoContentAsync(
            "Sickness.Close",
            $"api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            request, cancellationToken);

    public Task<ApiResult<GetCurrentSicknessAbsencesResponseModel>> GetCurrentSicknessAbsencesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<GetCurrentSicknessAbsencesResponseModel>(
            "Sickness.GetCurrentAbsences",
            $"api/companies/{companyId}/sickness-records/current", cancellationToken);

    public Task<ApiResult<GetTeamSicknessTodayResponseModel>> GetTeamSicknessTodayAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken = default) =>
        GetAsync<GetTeamSicknessTodayResponseModel>(
            "Sickness.GetTeamToday",
            $"api/companies/{companyId}/employees/{managerId}/team-sickness-today", cancellationToken);

    public Task<ApiResult<GetMissingFitNotesResponseModel>> GetMissingFitNotesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<GetMissingFitNotesResponseModel>(
            "Sickness.GetMissingFitNotes",
            $"api/companies/{companyId}/sickness-evidence-requests/missing", cancellationToken);

    /// <summary>Throwing variant for <see cref="WidgetSourceLoader"/>, which owns failure logging and presentation.</summary>
    public Task<GetMissingFitNotesResponseModel?> GetMissingFitNotesOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetMissingFitNotesResponseModel>(
            $"api/companies/{companyId}/sickness-evidence-requests/missing", HrApiJsonOptions.Default, cancellationToken);
}
