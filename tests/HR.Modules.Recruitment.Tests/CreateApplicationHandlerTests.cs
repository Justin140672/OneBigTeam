using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateApplicationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_Application_On_First_Active_NonTerminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value!.CurrentStageId);
        Assert.Equal(vacancy.Id, result.Value.VacancyId);
        Assert.Equal(candidate.Id, result.Value.CandidateId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Seeds_Default_Stages_When_Company_Has_None_Yet()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = Guid.NewGuid(), CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Applied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Source_Direct_And_No_Recruiter_Id()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.Direct,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.Direct, result.Value!.Source);
        Assert.Null(result.Value.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_With_Source_ExternalRecruiter_And_Valid_Recruiter_Id()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.ExternalRecruiter, result.Value!.Source);
        Assert.Equal(recruiter.Id, result.Value.SourceExternalRecruiterId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(recruiter.Id, saved.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Source_ExternalRecruiter_But_Recruiter_Id_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), otherCompanyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                Source = ApplicationSource.ExternalRecruiter,
                SourceExternalRecruiterId = recruiter.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Candidate_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(await db.Applications.ToListAsync());
    }

    // ---- Internal recruitment Ticket 1: submitted CV reference --------------------------------------

    private static CandidateDocument SubmittedCvDocument(
        Guid companyId, Guid candidateId, string fileName = "cv.pdf",
        CandidateDocumentKind kind = CandidateDocumentKind.Cv, DateTimeOffset? createdAt = null) =>
        CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, fileName, fileName, 2048, "application/pdf",
            $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}", Guid.NewGuid(), createdAt ?? Now, kind);

    private static async Task<(Guid CompanyId, Vacancy Vacancy, Candidate Candidate)> SeedVacancyAndCandidateAsync(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return (companyId, vacancy, candidate);
    }

    [Fact]
    public async Task HandleAsync_With_Valid_CvDocumentId_Persists_Reference_Returns_It_And_Audits()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        var cv = SubmittedCvDocument(companyId, candidate.Id);
        db.CandidateDocuments.Add(cv);
        await db.SaveChangesAsync();
        var audit = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();

        var result = await handler(db, audit).HandleAsync(
            new CreateApplicationRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                CandidateId = candidate.Id,
                CvDocumentId = cv.Id,
                PerformedByUserId = performedBy,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(cv.Id, result.Value!.CvDocumentId);

        var saved = await db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(cv.Id, saved.CvDocumentId);

        var evt = Assert.IsType<ApplicationCvReferenceChangedAuditEvent>(
            Assert.Single(audit.Published, e => e is ApplicationCvReferenceChangedAuditEvent));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(result.Value.Id, evt.ApplicationId);
        Assert.Equal(vacancy.Id, evt.VacancyId);
        Assert.Equal(candidate.Id, evt.CandidateId);
        Assert.Null(evt.PreviousCvDocumentId);
        Assert.Equal(cv.Id, evt.NewCvDocumentId);
        Assert.Equal(performedBy, evt.ChangedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Without_CvDocumentId_Leaves_Reference_Null_And_Publishes_No_Cv_Audit()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        // The candidate HAS a CV, but none was selected — it must not be picked up implicitly.
        db.CandidateDocuments.Add(SubmittedCvDocument(companyId, candidate.Id));
        await db.SaveChangesAsync();
        var audit = new FakeAuditPublisher();

        var result = await handler(db, audit).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvDocumentId);
        Assert.Null((await db.Applications.AsNoTracking().SingleAsync()).CvDocumentId);
        Assert.DoesNotContain(audit.Published, e => e is ApplicationCvReferenceChangedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Unknown_CvDocumentId_And_Creates_No_Application()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        var audit = new FakeAuditPublisher();

        var result = await handler(db, audit).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id, CvDocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(Application.CvDocumentNotFoundMessage, result.Error.Message);
        Assert.Empty(await db.Applications.ToListAsync());
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Rejects_CvDocument_From_Another_Company_As_Not_Found_And_Creates_No_Application()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        var foreignCv = SubmittedCvDocument(Guid.NewGuid(), candidate.Id);
        db.CandidateDocuments.Add(foreignCv);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id, CvDocumentId = foreignCv.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(Application.CvDocumentNotFoundMessage, result.Error.Message);
        Assert.Empty(await db.Applications.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Rejects_CvDocument_Of_Another_Candidate_And_Creates_No_Application()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var otherCandidatesCv = SubmittedCvDocument(companyId, otherCandidate.Id);
        db.Candidates.Add(otherCandidate);
        db.CandidateDocuments.Add(otherCandidatesCv);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id, CvDocumentId = otherCandidatesCv.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("The selected CV document belongs to a different candidate.", result.Error.Message);
        Assert.Empty(await db.Applications.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Rejects_Non_Cv_Document_And_Creates_No_Application()
    {
        await using var db = BuildContext();
        var (companyId, vacancy, candidate) = await SeedVacancyAndCandidateAsync(db);
        var coverLetter = SubmittedCvDocument(companyId, candidate.Id, "cover.pdf", CandidateDocumentKind.Other);
        db.CandidateDocuments.Add(coverLetter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id, CvDocumentId = coverLetter.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("not a CV", result.Error.Message);
        Assert.Empty(await db.Applications.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Two_Applications_For_Same_Candidate_Keep_Their_Own_Different_Cvs()
    {
        // Acceptance: each application fixes the CV it was submitted with, independently of the other.
        await using var db = BuildContext();
        var (companyId, vacancyA, candidate) = await SeedVacancyAndCandidateAsync(db);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var cvForA = SubmittedCvDocument(companyId, candidate.Id, "cv-engineering.pdf");
        var cvForB = SubmittedCvDocument(companyId, candidate.Id, "cv-design.pdf", createdAt: Now.AddMinutes(5));
        db.Vacancies.Add(vacancyB);
        db.CandidateDocuments.AddRange(cvForA, cvForB);
        await db.SaveChangesAsync();

        var resultA = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancyA.Id, CandidateId = candidate.Id, CvDocumentId = cvForA.Id },
            CancellationToken.None);
        var resultB = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancyB.Id, CandidateId = candidate.Id, CvDocumentId = cvForB.Id },
            CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.True(resultB.IsSuccess);
        Assert.Equal(cvForA.Id, resultA.Value!.CvDocumentId);
        Assert.Equal(cvForB.Id, resultB.Value!.CvDocumentId);

        var saved = await db.Applications.AsNoTracking().ToDictionaryAsync(a => a.VacancyId);
        Assert.Equal(cvForA.Id, saved[vacancyA.Id].CvDocumentId);
        Assert.Equal(cvForB.Id, saved[vacancyB.Id].CvDocumentId);
    }

    private static CreateApplicationHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new Infrastructure.FakeAuditPublisher(), new RecruitmentStageSeeder(db));

    private static CreateApplicationHandler handler(RecruitmentDbContext db, FakeAuditPublisher audit) =>
        new(db, new FakeClock(FixedUtcNow), audit, new RecruitmentStageSeeder(db));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
