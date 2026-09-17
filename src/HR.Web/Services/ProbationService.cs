using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class ProbationService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    // A missing probation record is a legitimate, expected outcome for an employee with no
    // probation period — 404 is treated as "no record" here, not as a failure.
    public async Task<ProbationRecordModel?> GetProbationRecordByEmployeeAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ProbationRecordModel>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/probation-record", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success || result.FailureKind == ApiFailureKind.NotFound ? result.Value : null;
    }

    public async Task<MyProbationStatusModel?> GetMyProbationStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<MyProbationStatusModel>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/me/probation-status", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<ProbationStatusModel?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ProbationStatusModel>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/probation-status", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<ProbationReviewDetailModel?> GetProbationReviewAsync(
        Guid companyId,
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ProbationReviewDetailModel>(
            ct => Http.GetAsync($"api/companies/{companyId}/probation-reviews/{reviewId}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<bool> CompleteReviewAsync(
        Guid companyId,
        Guid probationRecordId,
        Guid reviewId,
        Guid completedByEmployeeId,
        string? notes,
        string? outcome = null,
        DateOnly? decisionDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews/{reviewId}/complete",
                new { CompletedByEmployeeId = completedByEmployeeId, Notes = notes, Outcome = outcome, DecisionDate = decisionDate },
                ct),
            cancellationToken: cancellationToken);
        return result.Success;
    }

    public async Task<IReadOnlyList<UpcomingProbationReviewItem>> GetUpcomingReviewsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<UpcomingProbationReviewsResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/probation-reviews/upcoming", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? (result.Value?.Items ?? []) : [];
    }

    // DSH-03: non-swallowing sibling of GetUpcomingReviewsAsync.
    public async Task<IReadOnlyList<UpcomingProbationReviewItem>> GetUpcomingReviewsOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await Http.GetFromJsonAsync<UpcomingProbationReviewsResponse>(
            $"api/companies/{companyId}/probation-reviews/upcoming", HrApiJsonOptions.Default, cancellationToken);
        return response?.Items ?? [];
    }

    /// <summary>
    /// Ticket 17: HR Administrator "administrative correction" edit — manager, expected end date,
    /// notes ONLY (status/extension/decision fields are workflow-owned and never sent here). See
    /// UpdateProbationRecordApiRequest and HR.Web.Components.Pages.EditSectionBase for the
    /// optimistic-concurrency contract: ExpectedVersion must be the Version last loaded, and the
    /// caller must distinguish HTTP 409 (stale — show the conflict banner, do not overwrite until
    /// the user explicitly reloads) from every other failure.
    /// </summary>
    public async Task<ApiSaveResult> UpdateProbationRecordAsync(
        Guid companyId, UpdateProbationRecordApiRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<UpdateProbationRecordApiResponse>(
            ct => Http.PutAsJsonAsync(
                $"api/companies/{companyId}/probation-records/{request.ProbationRecordId}", request, ct),
            HrApiJsonOptions.Default, cancellationToken);

        return result.Success
            ? ApiSaveResult.Ok(result.Value?.Version)
            : ApiSaveResult.Fail(result.DisplayMessage ?? "Failed to save the probation record.", result.IsConcurrencyConflict);
    }

    public async Task<IReadOnlyList<ProbationReviewModel>> GetProbationReviewsAsync(
        Guid companyId,
        Guid probationRecordId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ProbationReviewsResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/probation-records/{probationRecordId}/reviews", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? (result.Value?.Items ?? []) : [];
    }
}
