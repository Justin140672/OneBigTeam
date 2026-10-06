using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// API arrange/probe helpers for internal vacancy offers: moves an INTERNAL application to the point
/// where an offer can be made (interview scheduled and passed), makes/revises the offer with the
/// internal terms (manager decision, currency, start date, deadline), lets the employee respond through
/// the employee endpoint, and exposes raw variants for the security probes.
/// </summary>
internal static class InternalOfferApi
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    public sealed record OfferInput(
        Guid? ManagerId = null,
        bool NoManager = false,
        DateOnly? StartDate = null,
        decimal Salary = 42000m,
        string Currency = "GBP",
        DateOnly? ResponseDeadline = null,
        decimal? HoursPerWeek = null,
        decimal? Fte = null,
        string? Notes = null);

    public sealed record OfferTerms(int OfferVersion, string? OfferResponseStatus);

    public sealed record InternalOfferView(bool IsOfferRecipient, bool CanRespond, string? CannotRespondReason, OfferTerms Terms);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static async Task ReachOfferStageAsync(HttpClient recruiterApi, Guid vacancyId, Guid applicationId)
    {
        var scheduleResponse = await recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies/{vacancyId}/applications/{applicationId}/interviews",
            new
            {
                companyId = AcmeId,
                vacancyId,
                applicationId,
                interviewerEmployeeId = InternalVacancyApplyApi.JamesId,
                scheduledAt = DateTimeOffset.UtcNow.AddDays(3),
            });
        Assert.True(scheduleResponse.IsSuccessStatusCode,
            $"Schedule interview failed with {scheduleResponse.StatusCode}: {await scheduleResponse.Content.ReadAsStringAsync()}");
        var interview = await scheduleResponse.Content.ReadFromJsonAsync<IdOnly>();
        Assert.NotNull(interview);

        var outcomeResponse = await recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interview!.Id}/outcome",
            new
            {
                companyId = AcmeId,
                vacancyId,
                applicationId,
                interviewId = interview.Id,
                outcome = "Passed",
            });
        Assert.True(outcomeResponse.IsSuccessStatusCode,
            $"Record interview outcome failed with {outcomeResponse.StatusCode}: {await outcomeResponse.Content.ReadAsStringAsync()}");
    }

    public static Task<HttpResponseMessage> PostOfferAsync(
        HttpClient recruiterApi, Guid vacancyId, Guid applicationId, OfferInput input) =>
        recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            new
            {
                companyId = AcmeId,
                vacancyId,
                applicationId,
                offeredSalary = input.Salary,
                offeredSalaryFrequency = "Annual",
                proposedStartDate = FormatDate(input.StartDate ?? Today),
                offerNotes = input.Notes,
                proposedManagerId = input.NoManager ? null : input.ManagerId,
                noManager = input.NoManager,
                currency = input.Currency,
                hoursPerWeek = input.HoursPerWeek,
                fte = input.Fte,
                responseDeadline = input.ResponseDeadline is { } deadline ? FormatDate(deadline) : null,
            });

    public static async Task MakeOfferAsync(
        HttpClient recruiterApi, Guid vacancyId, Guid applicationId, OfferInput input)
    {
        var response = await PostOfferAsync(recruiterApi, vacancyId, applicationId, input);
        Assert.True(response.IsSuccessStatusCode,
            $"Make internal offer failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static Task<HttpResponseMessage> GetInternalOfferRawAsync(HttpClient api, Guid companyId, Guid applicationId) =>
        api.GetAsync($"/api/companies/{companyId}/internal-offers/{applicationId}");

    public static async Task<InternalOfferView> GetInternalOfferAsync(HttpClient api, Guid applicationId)
    {
        var response = await GetInternalOfferRawAsync(api, AcmeId, applicationId);
        Assert.True(response.IsSuccessStatusCode,
            $"GET internal offer failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var view = await response.Content.ReadFromJsonAsync<InternalOfferView>();
        Assert.NotNull(view);
        return view!;
    }

    public static Task<HttpResponseMessage> PostEmployeeResponseRawAsync(
        HttpClient api, Guid companyId, Guid applicationId, string decision, int offerVersion, string? reason = null) =>
        api.PostAsJsonAsync(
            $"/api/companies/{companyId}/internal-offers/{applicationId}/response",
            new { companyId, applicationId, decision, offerVersion, reason });

    public static async Task RespondAsEmployeeAsync(
        HttpClient employeeApi, Guid applicationId, string decision, string? reason = null)
    {
        var view = await GetInternalOfferAsync(employeeApi, applicationId);
        var response = await PostEmployeeResponseRawAsync(
            employeeApi, AcmeId, applicationId, decision, view.Terms.OfferVersion, reason);
        Assert.True(response.IsSuccessStatusCode,
            $"Employee {decision} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task MakeAcceptedOfferAsync(
        HttpClient recruiterApi, HttpClient employeeApi, Guid vacancyId, Guid applicationId, OfferInput input)
    {
        await ReachOfferStageAsync(recruiterApi, vacancyId, applicationId);
        await MakeOfferAsync(recruiterApi, vacancyId, applicationId, input);
        await RespondAsEmployeeAsync(employeeApi, applicationId, "Accept");
    }

    public static async Task AssertClientErrorAsync(HttpResponseMessage response, string because)
    {
        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500) return;
        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"{because}: expected a 4xx rejection, got {status} {response.StatusCode}. Body: {body}");
    }

    public static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string because) =>
        await InternalRecruitmentJourneyApi.AssertStatusAsync(response, expected, because);

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record IdOnly(Guid Id);
}
