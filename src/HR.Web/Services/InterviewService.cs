using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class InterviewService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListInterviewsForVacancyResponse?> ListInterviewsForVacancyAsync(Guid companyId, Guid vacancyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListInterviewsForVacancyResponse>(
                $"api/companies/{companyId}/vacancies/{vacancyId}/interviews", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(ScheduleInterviewResponse? Result, string? Error)> ScheduleInterviewAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, ScheduleInterviewRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ScheduleInterviewResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to schedule interview."));
    }

    public async Task<(RecordInterviewOutcomeResponse? Result, string? Error)> RecordInterviewOutcomeAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId, string outcome, string? notes)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interviewId}/outcome",
            new RecordInterviewOutcomeRequest(companyId, vacancyId, applicationId, interviewId, outcome, notes));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<RecordInterviewOutcomeResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to record interview outcome."));
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
