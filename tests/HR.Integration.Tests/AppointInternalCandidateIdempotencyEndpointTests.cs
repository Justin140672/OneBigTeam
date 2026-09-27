using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 7: retries and partial failures. The Recruitment and Employees modules
/// commit separately; the employee change is keyed by the stable source reference
/// "recruitment:application:{id}", so a retry — or a request after the Employees side committed but
/// the Recruitment side did not — completes the application without recording a second change.
/// Also verifies an appointment counts as a hire in the recruitment reports' Internal filter.
/// </summary>
[Collection("Integration")]
public class AppointInternalCandidateIdempotencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AppointInternalCandidateIdempotencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Retry_After_Completion_Is_Refused_And_Records_Nothing_New()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var first = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var promotionId = (await ReadAppointResponseAsync(first)).PromotionId;

        // Different values on the retry must not be applied either.
        var second = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, noManager: true));
        var third = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Equal(promotionId, promotion.Id);
        Assert.Equal(s.World.NewManagerId, (await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);

        var history = await GetStageHistoryAsync(_factory, s.ApplicationId);
        Assert.Single(history, h => h.NewStageId == s.HiredStageId);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(promotionId, application.AppointmentPromotionId);
    }

    [Fact]
    public async Task Partial_Failure_Employees_Committed_Recruitment_Not_Is_Completed_Without_A_Second_Change()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        // 1. Simulate the interrupted request: Recruitment saved Pending, then Employees recorded and
        //    applied the change, then the process died before Recruitment completed.
        await MarkAppointmentPendingAsync(_factory, s.ApplicationId, s.EmployeeId, DateTimeOffset.UtcNow.AddMinutes(-1));

        InternalAppointmentResult recorded;
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IEmployeeInternalAppointmentService>();
            var result = await service.AppointAsync(
                new InternalAppointmentRequest(
                    companyId, s.EmployeeId, s.World.Target.PositionProfileId, Today, s.World.NewManagerId,
                    s.SourceReference, "Internal appointment: Engineering Manager", Guid.NewGuid(),
                    ConfirmBackdatedEffectiveDate: false, Compensation: null),
                CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            recorded = result.Value!;
        }

        Assert.True(recorded.IsApplied);
        var pending = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Pending, pending.AppointmentStatus);
        Assert.Equal(s.OfferStageId, pending.CurrentStageId);

        // 2. HR retries (even with different values): the recorded change is completed as recorded.
        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, noManager: true, effectiveDate: Today.AddDays(5)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(recorded.PromotionId, body.PromotionId);
        Assert.Equal(Today, body.EffectiveDate);
        Assert.Equal(s.World.NewManagerId, body.ManagerId);

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Equal(recorded.PromotionId, promotion.Id);
        Assert.Equal(s.World.NewManagerId, (await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(recorded.PromotionId, application.AppointmentPromotionId);
        Assert.Equal(s.HiredStageId, application.CurrentStageId);
        Assert.Single(await GetStageHistoryAsync(_factory, s.ApplicationId), h => h.NewStageId == s.HiredStageId);
    }

    [Fact]
    public async Task Pending_Without_A_Recorded_Change_Is_Appointed_Normally_Exactly_Once()
    {
        // Interrupted before the Employees module was reached: nothing recorded, so the retry performs
        // the appointment itself.
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        await MarkAppointmentPendingAsync(_factory, s.ApplicationId, s.EmployeeId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Equal(InternalAppointmentStatus.Completed, (await GetApplicationAsync(_factory, s.ApplicationId)).AppointmentStatus);
    }

    [Fact]
    public async Task Pipeline_Actions_Are_Refused_While_An_Appointment_Is_Pending()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        await MarkAppointmentPendingAsync(_factory, s.ApplicationId, s.EmployeeId, DateTimeOffset.UtcNow);
        var baseUrl = $"/api/companies/{companyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}";

        var reject = await client.PostAsJsonAsync($"{baseUrl}/reject", new { rejectionReason = "No longer needed." });
        var withdraw = await client.DeleteAsync(baseUrl);

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withdraw.StatusCode);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Pending, application.AppointmentStatus);
        Assert.Equal(s.OfferStageId, application.CurrentStageId);
        Assert.Null(application.WithdrawnAt);
    }

    [Fact]
    public async Task Pipeline_Report_With_Internal_Filter_Counts_The_Appointment_As_A_Hire()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var appoint = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));
        Assert.Equal(HttpStatusCode.OK, appoint.StatusCode);

        var internalReport = await client.GetFromJsonAsync<PipelinePayload>(
            $"/api/companies/{companyId}/reporting/recruitment-pipeline?groupBy=Vacancy&isInternal=true");
        var row = Assert.Single(internalReport!.Items);
        Assert.Equal(s.VacancyId.ToString(), row.GroupKey);
        Assert.Equal(1, row.Candidates);
        Assert.Equal(1, row.Hires);
        Assert.Equal(1, row.Interviews);

        var externalReport = await client.GetFromJsonAsync<PipelinePayload>(
            $"/api/companies/{companyId}/reporting/recruitment-pipeline?groupBy=Vacancy&isInternal=false");
        Assert.Equal(0, externalReport!.Items.Sum(i => i.Hires));

        // The pipeline summary shows the internal application sitting on the Hired stage.
        var summary = await client.GetFromJsonAsync<SummaryPayload>(
            $"/api/companies/{companyId}/reporting/recruitment-pipeline-summary?includeClosed=true&isInternal=true");
        var summaryRow = Assert.Single(summary!.Vacancies, v => v.VacancyId == s.VacancyId);
        Assert.Equal(1, summaryRow.CandidatesByStage.GetValueOrDefault(s.HiredStageId));
    }

    private sealed record PipelinePayload(List<PipelineItemPayload> Items);

    private sealed record PipelineItemPayload(string GroupKey, string GroupLabel, int Vacancies, int Candidates, int Interviews, int Offers, int Hires);

    private sealed record SummaryPayload(List<SummaryRowPayload> Vacancies);

    private sealed record SummaryRowPayload(Guid VacancyId, int CandidateCount, Dictionary<Guid, int> CandidatesByStage);
}
