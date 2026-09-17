using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class InterviewService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<GetInterviewsTodayCountResponse?> GetInterviewsTodayCountAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetInterviewsTodayCountResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/interviews/today-count", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    // DSH-03: non-swallowing sibling of GetInterviewsTodayCountAsync.
    public Task<GetInterviewsTodayCountResponse?> GetInterviewsTodayCountOrThrowAsync(Guid companyId) =>
        Http.GetFromJsonAsync<GetInterviewsTodayCountResponse>(
            $"api/companies/{companyId}/interviews/today-count", HrApiJsonOptions.Default);

    // DSH-04: authoritative "interviews requiring action" metric (Pending outcome, scheduled at or
    // before end of today). Non-swallowing (DSH-03 style) for the dashboard's per-source failure UI.
    public Task<InterviewsRequiringActionMetricResponse?> GetInterviewsRequiringActionMetricOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<InterviewsRequiringActionMetricResponse>(
            $"api/companies/{companyId}/recruitment/metrics/interviews-requiring-action", HrApiJsonOptions.Default, cancellationToken);

    public async Task<GetUpcomingInterviewsResponse?> GetUpcomingInterviewsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetUpcomingInterviewsResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/interviews/upcoming", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    // Non-swallowing sibling of GetUpcomingInterviewsAsync, for callers (e.g.
    // UpcomingInterviewsWidget) that use WidgetSourceLoader to distinguish a failed load from a
    // genuine empty result rather than collapsing both into null.
    public Task<GetUpcomingInterviewsResponse?> GetUpcomingInterviewsOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetUpcomingInterviewsResponse>(
            $"api/companies/{companyId}/interviews/upcoming", HrApiJsonOptions.Default, cancellationToken);

    public async Task<ListInterviewsForVacancyResponse?> ListInterviewsForVacancyAsync(Guid companyId, Guid vacancyId)
    {
        var result = await ApiResponseReader.ExecuteAsync<ListInterviewsForVacancyResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/vacancies/{vacancyId}/interviews", ct),
            HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<(ScheduleInterviewResponse? Result, string? Error)> ScheduleInterviewAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, ScheduleInterviewRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews", request);
        var result = await ApiResponseReader.ReadJsonAsync<ScheduleInterviewResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to schedule interview."));
    }

    public async Task<(RecordInterviewOutcomeResponse? Result, string? Error)> RecordInterviewOutcomeAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId, string outcome, string? notes)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interviewId}/outcome",
            new RecordInterviewOutcomeRequest(companyId, vacancyId, applicationId, interviewId, outcome, notes));
        var result = await ApiResponseReader.ReadJsonAsync<RecordInterviewOutcomeResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to record interview outcome."));
    }
}
