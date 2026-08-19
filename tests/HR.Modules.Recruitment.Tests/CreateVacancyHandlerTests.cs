using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Vacancy_In_Draft_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        var positionProfileId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = companyId,
                PositionProfileId = positionProfileId,
                AdvertTitle       = "Senior Software Engineer",
                HiringManagerId   = hiringManagerId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(positionProfileId, result.Value.PositionProfileId);
        Assert.Equal("Senior Software Engineer", result.Value.AdvertTitle);
        Assert.Equal(VacancyStatus.Draft, result.Value.Status);
        Assert.Equal(hiringManagerId, result.Value.HiringManagerId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
        // Round-trip through the (InMemory) RecruitmentDbContext, not just the handler's response DTO.
        Assert.Equal(positionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Propagates_AssignedRecruiterId_To_Response()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        // Ticket #81: AssignedRecruiterId now references ExternalRecruiter (in this same module/schema)
        // rather than an unvalidated Employee id, so the handler validates existence/company-ownership —
        // a real, active ExternalRecruiter row must exist for this to succeed.
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, FixedUtcNow);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId           = companyId,
                PositionProfileId   = Guid.NewGuid(),
                AdvertTitle         = "Senior Software Engineer",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(recruiter.Id, result.Value!.AssignedRecruiterId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(recruiter.Id, saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Creates_Vacancy_Without_AssignedRecruiterId_Succeeds_Since_It_Is_Optional()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId           = Guid.NewGuid(),
                PositionProfileId   = Guid.NewGuid(),
                AdvertTitle         = "Backend Engineer",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = null,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AssignedRecruiterId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_AssignedRecruiter_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, FixedUtcNow);
        recruiter.SetActiveStatus(false, FixedUtcNow);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId           = companyId,
                PositionProfileId   = Guid.NewGuid(),
                AdvertTitle         = "Backend Engineer",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("inactive", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acme Recruiting", result.Error.Message);
        Assert.Empty(db.Vacancies);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AssignedRecruiter_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), otherCompanyId, "Acme Recruiting", null, null, null, null, null, FixedUtcNow);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId           = companyId,
                PositionProfileId   = Guid.NewGuid(),
                AdvertTitle         = "Backend Engineer",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(db.Vacancies);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AssignedRecruiter_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId           = Guid.NewGuid(),
                PositionProfileId   = Guid.NewGuid(),
                AdvertTitle         = "Backend Engineer",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(db.Vacancies);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await handler(db, new FakePositionProfileReader(exists: false)).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                AdvertTitle       = "Senior Software Engineer",
                HiringManagerId   = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(db.Vacancies);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var reader = new FakePositionProfileReader(
            matchingCompanyId: Guid.NewGuid(), // a different company than the request below
            matchingPositionProfileId: positionProfileId);

        var result = await handler(db, reader).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = companyId,
                PositionProfileId = positionProfileId,
                AdvertTitle       = "Senior Software Engineer",
                HiringManagerId   = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(db.Vacancies);
    }

    [Fact]
    public async Task HandleAsync_Trims_AdvertTitle_And_AdvertDescription()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                AdvertTitle       = "  Backend Engineer  ",
                AdvertDescription = "  Own the payments platform  ",
                HiringManagerId   = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Backend Engineer", result.Value!.AdvertTitle);
        Assert.Equal("Own the payments platform", result.Value.AdvertDescription);
    }

    [Fact]
    public async Task HandleAsync_Creates_Vacancy_Without_AdvertTitle_Succeeds_With_Null_AdvertTitle()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
                AdvertTitle       = null,
                HiringManagerId   = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AdvertTitle);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AdvertTitle);
    }

    [Fact]
    public void CreateVacancyRequest_Has_No_Location_Field()
    {
        // Location was removed entirely from the domain — assert (via reflection, so this test would
        // fail loudly if the field were ever reintroduced) that CreateVacancyRequest has no Location
        // member of any kind, rather than relying solely on compile-time enforcement.
        var members = typeof(CreateVacancyRequest).GetMembers()
            .Select(m => m.Name);

        Assert.DoesNotContain(members, name => name.Contains("Location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_PositionProfileId_Persists_And_Reads_Back_As_A_Valid_NonNullable_Guid()
    {
        // Proves the NOT NULL PositionProfileId migration/config doesn't break the normal Create flow:
        // a vacancy created via the handler round-trips through the DbContext with a valid,
        // non-nullable PositionProfileId, with no exception thrown reading it back.
        await using var db = BuildContext();
        var positionProfileId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId         = Guid.NewGuid(),
                PositionProfileId = positionProfileId,
                AdvertTitle       = "Backend Engineer",
                HiringManagerId   = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == result.Value!.Id);
        Guid nonNullablePositionProfileId = saved.PositionProfileId;
        Assert.Equal(positionProfileId, nonNullablePositionProfileId);
        Assert.NotEqual(Guid.Empty, nonNullablePositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Seeds_Default_RecruitmentStages_For_The_Companys_First_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var result = await handler(db, new FakePositionProfileReader()).HandleAsync(
            new CreateVacancyRequest { CompanyId = companyId, PositionProfileId = Guid.NewGuid(), HiringManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Duplicate_Stages_When_Company_Already_Has_A_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reader = new FakePositionProfileReader();

        await handler(db, reader).HandleAsync(
            new CreateVacancyRequest { CompanyId = companyId, PositionProfileId = Guid.NewGuid(), HiringManagerId = Guid.NewGuid() },
            CancellationToken.None);
        await handler(db, reader).HandleAsync(
            new CreateVacancyRequest { CompanyId = companyId, PositionProfileId = Guid.NewGuid(), HiringManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
    }

    private static CreateVacancyHandler handler(RecruitmentDbContext db, HR.Modules.Employees.Contracts.IPositionProfileReader? positionProfileReader = null) =>
        new(db, new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader(), new HR.Modules.Recruitment.Services.RecruitmentStageSeeder(db));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
