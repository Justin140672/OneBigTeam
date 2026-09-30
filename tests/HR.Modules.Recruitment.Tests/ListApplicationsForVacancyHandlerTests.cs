using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListApplicationsForVacancyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Applications_For_Vacancy_With_Candidate_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Emma", result.Value.Items[0].CandidateFirstName);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value.Items[0].CurrentStageId);
        Assert.False(result.Value.Items[0].IsWithdrawn);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_StageId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, Now);

        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateA.Id, stages.ApplicationReceived.Id, null, Now);
        var cvReview = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateB.Id, stages.CvReview.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidateA, candidateB);
        db.Applications.AddRange(applied, cvReview);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id, StageId = stages.CvReview.Id },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Liam", result.Value.Items[0].CandidateFirstName);
    }

    [Fact]
    public async Task HandleAsync_Flags_Withdrawn_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.Value!.Items[0].IsWithdrawn);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Applications_For_Other_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);

        db.Vacancies.AddRange(vacancyA, vacancyB);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancyB.Id },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    // ----- Internal recruitment Ticket 6: IsInternal / EmployeeId + ?isInternal filter -----

    private sealed record MixedSeed(
        Guid CompanyId,
        Guid VacancyId,
        Guid EmployeeId,
        Guid InternalId,
        Guid DirectId,
        Guid LegacyNullSourceId,
        Guid HiredExternalId);

    private static async Task<MixedSeed> SeedMixedAsync(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);

        var (_, internalApp) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, employeeId, Now);
        var (_, directApp)   = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, ApplicationSource.Direct, Now.AddMinutes(1));
        var (_, legacyApp)   = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.CvReview.Id, null, Now.AddMinutes(2), "Noah", "Patel");
        var (_, hiredApp)    = InternalApplicationTestData.AddHiredExternal(db, companyId, vacancy.Id, stages.Hired.Id, ApplicationSource.Direct, Guid.NewGuid(), Now.AddMinutes(3));
        await db.SaveChangesAsync();

        return new MixedSeed(companyId, vacancy.Id, employeeId, internalApp.Id, directApp.Id, legacyApp.Id, hiredApp.Id);
    }

    [Fact]
    public async Task HandleAsync_Populates_IsInternal_And_EmployeeId_Only_For_Internal_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items.ToDictionary(i => i.Id);
        Assert.Equal(4, items.Count);

        Assert.True(items[seed.InternalId].IsInternal);
        Assert.Equal(seed.EmployeeId, items[seed.InternalId].EmployeeId);

        foreach (var externalId in new[] { seed.DirectId, seed.LegacyNullSourceId, seed.HiredExternalId })
        {
            Assert.False(items[externalId].IsInternal);
            Assert.Null(items[externalId].EmployeeId);
        }
    }

    [Fact]
    public async Task HandleAsync_IsInternal_True_Returns_Only_Internal_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, IsInternal = true },
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(seed.InternalId, item.Id);
        Assert.True(item.IsInternal);
        Assert.Equal(seed.EmployeeId, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_IsInternal_False_Returns_External_Including_Legacy_Null_Source_And_Hired_External()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, IsInternal = false },
            CancellationToken.None);

        Assert.Equal(
            new[] { seed.DirectId, seed.LegacyNullSourceId, seed.HiredExternalId }.OrderBy(x => x),
            result.Value!.Items.Select(i => i.Id).OrderBy(x => x));
        Assert.All(result.Value.Items, i =>
        {
            Assert.False(i.IsInternal);
            Assert.Null(i.EmployeeId);
        });
    }

    [Fact]
    public async Task HandleAsync_IsInternal_Null_Returns_All_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, IsInternal = null },
            CancellationToken.None);

        Assert.Equal(4, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_IsInternal_Filter_Combines_With_StageId_Filter()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);
        var receivedStageId = (await db.Applications.SingleAsync(a => a.Id == seed.InternalId)).CurrentStageId;

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, StageId = receivedStageId, IsInternal = false },
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(seed.DirectId, item.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Direct")]
    public async Task HandleAsync_Hired_External_Candidate_Linked_To_Employee_Is_Not_Internal(string? source)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        var (_, hired) = InternalApplicationTestData.AddHiredExternal(
            db, companyId, vacancy.Id, stages.Hired.Id, InternalApplicationTestData.ParseSource(source), Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        var internalOnly = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id, IsInternal = true },
            CancellationToken.None);
        var all = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.Empty(internalOnly.Value!.Items);
        var item = Assert.Single(all.Value!.Items);
        Assert.Equal(hired.Id, item.Id);
        Assert.False(item.IsInternal);
        Assert.Null(item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Internal_Appointment_Status_And_Effective_Date()
    {
        // Internal recruitment Ticket 7.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineering Manager", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        var completedEmployee = Guid.NewGuid();
        var (_, completed) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.Offer.Id, completedEmployee, Now, "Aisha", "Khan");
        completed.BeginInternalAppointment(completedEmployee, Guid.NewGuid(), Now);
        completed.CompleteInternalAppointment(stages.Hired.Id, Guid.NewGuid(), new DateOnly(2026, 8, 3), Now);
        var pendingEmployee = Guid.NewGuid();
        var (_, pending) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.Offer.Id, pendingEmployee, Now, "Ben", "Cole");
        pending.BeginInternalAppointment(pendingEmployee, Guid.NewGuid(), Now);
        var (_, untouched) = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.CvReview.Id, ApplicationSource.Direct, Now);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items.ToDictionary(i => i.Id);
        Assert.Equal("Completed", items[completed.Id].InternalAppointmentStatus);
        Assert.Equal(new DateOnly(2026, 8, 3), items[completed.Id].InternalAppointmentEffectiveDate);
        Assert.Equal("Pending", items[pending.Id].InternalAppointmentStatus);
        Assert.Null(items[pending.Id].InternalAppointmentEffectiveDate);
        Assert.Null(items[untouched.Id].InternalAppointmentStatus);
        Assert.Null(items[untouched.Id].InternalAppointmentEffectiveDate);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
