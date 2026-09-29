using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateCandidateApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 3: handler-level coverage (EF InMemory — the intake skips the
/// transaction and advisory lock when the provider is not relational) for creating a new candidate,
/// optional CV document and their application in one call via
/// <see cref="CandidateApplicationIntake"/>. The genuine concurrent duplicate-email race is covered
/// against PostgreSQL in <see cref="CreateCandidateApplicationConcurrencyTests"/>.
/// </summary>
public class CreateCandidateApplicationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required RecruitmentDbContext Db { get; init; }
        public required FakeCandidateDocumentStorageService Storage { get; init; }
        public required FakeAuditPublisher Audit { get; init; }
        public required CreateCandidateApplicationHandler Handler { get; init; }
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Harness BuildHarness(RecruitmentDbContext db, CandidateDocumentUploadOptions? options = null)
    {
        var storage = new FakeCandidateDocumentStorageService();
        var audit = new FakeAuditPublisher();
        var intake = new CandidateApplicationIntake(
            db,
            storage,
            Options.Create(options ?? new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            audit,
            new RecruitmentStageSeeder(db),
            NullLogger<CandidateApplicationIntake>.Instance);

        return new Harness
        {
            Db = db,
            Storage = storage,
            Audit = audit,
            Handler = new CreateCandidateApplicationHandler(intake),
        };
    }

    private static IFormFile FakeFile(string fileName, string contentType, int size) =>
        new FormFile(new MemoryStream(new byte[size]), 0, size, "CvFile", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static IFormFile FakePdf(string fileName = "emma-cv.pdf", int size = 2048) =>
        FakeFile(fileName, "application/pdf", size);

    private static CreateCandidateApplicationRequest Request(
        Guid companyId,
        Guid vacancyId,
        string email = "emma.clarke@example.com",
        string firstName = "Emma",
        string lastName = "Clarke",
        IFormFile? cv = null,
        ApplicationSource? source = null,
        Guid? recruiterId = null,
        string? notes = null) =>
        new()
        {
            CompanyId                 = companyId,
            VacancyId                 = vacancyId,
            FirstName                 = firstName,
            LastName                  = lastName,
            Email                     = email,
            Phone                     = "07700 900123",
            Notes                     = notes,
            Source                    = source,
            SourceExternalRecruiterId = recruiterId,
            CvFile                    = cv,
        };

    private static async Task<(Vacancy Vacancy, RecruitmentStageTestData.SeededStages Stages)> SeedVacancyWithStagesAsync(
        RecruitmentDbContext db, Guid companyId)
    {
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return (vacancy, stages);
    }

    private static async Task<Candidate> SeedCandidateAsync(
        RecruitmentDbContext db, Guid companyId, string email, bool active = true, DateTimeOffset? createdAt = null,
        string firstName = "Existing", string lastName = "Person")
    {
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, firstName, lastName, email, null, null, createdAt ?? Now.AddDays(-10));
        if (!active)
            candidate.Deactivate(Guid.NewGuid(), "No longer looking", Now.AddDays(-5));
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }

    private static async Task AssertNothingCreatedAsync(Harness h, int expectedCandidates = 0)
    {
        Assert.Equal(expectedCandidates, await h.Db.Candidates.CountAsync());
        Assert.Equal(0, await h.Db.Applications.CountAsync());
        Assert.Equal(0, await h.Db.CandidateDocuments.CountAsync());
        Assert.Equal(0, await h.Db.CandidateDocumentDeletionOperations.CountAsync());
        Assert.Empty(h.Storage.Uploads);
        Assert.Empty(h.Audit.Published);
    }


    [Fact]
    public async Task HandleAsync_Without_Cv_Creates_Candidate_And_Application_On_Initial_Stage()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, stages) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, email: "  emma.clarke@example.com  ", notes: "Met at a meetup"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result.DuplicateCandidate);
        Assert.True(result.Result.IsSuccess);
        var response = result.Result.Value!;
        Assert.Equal(companyId, response.CompanyId);
        Assert.Equal(vacancy.Id, response.VacancyId);
        Assert.Equal("Emma", response.FirstName);
        Assert.Equal("Clarke", response.LastName);
        Assert.Equal("emma.clarke@example.com", response.Email);
        Assert.Equal(stages.ApplicationReceived.Id, response.CurrentStageId);
        Assert.Null(response.CvDocumentId);
        Assert.Null(response.Source);
        Assert.Null(response.SourceExternalRecruiterId);
        Assert.Equal(Now, response.AppliedAt);

        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(response.CandidateId, candidate.Id);
        Assert.Equal(companyId, candidate.CompanyId);
        Assert.Equal("emma.clarke@example.com", candidate.Email);
        Assert.True(candidate.IsActive);

        var application = await db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(response.ApplicationId, application.Id);
        Assert.Equal(candidate.Id, application.CandidateId);
        Assert.Equal(vacancy.Id, application.VacancyId);
        Assert.Equal(stages.ApplicationReceived.Id, application.CurrentStageId);
        Assert.Null(application.CvDocumentId);

        Assert.Equal(0, await db.CandidateDocuments.CountAsync());
        Assert.Equal(0, await db.CandidateDocumentDeletionOperations.CountAsync());
        Assert.Empty(h.Storage.Uploads);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Places_Application_On_Lowest_DisplayOrder_Active_NonTerminal_Stage()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, stages) = await SeedVacancyWithStagesAsync(db, companyId);

        var first = await db.RecruitmentStages.SingleAsync(s => s.Id == stages.ApplicationReceived.Id);
        first.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(stages.CvReview.Id, result.Result.Value!.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Seeds_Default_Stages_When_Company_Has_None_Yet()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
        var firstStage = await db.RecruitmentStages
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .FirstAsync();
        Assert.Equal(firstStage.Id, result.Result.Value!.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_With_Cv_Creates_Cv_Document_Attaches_It_Uploads_Once_And_Confirms_Intent()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var performedBy = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf("emma-cv.pdf", 4096)),
            performedBy,
            CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        var response = result.Result.Value!;
        Assert.NotNull(response.CvDocumentId);

        var document = await db.CandidateDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(response.CvDocumentId, document.Id);
        Assert.Equal(response.CandidateId, document.CandidateId);
        Assert.Equal(companyId, document.CompanyId);
        Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
        Assert.Equal(CandidateApplicationIntake.SubmittedCvTitle, document.Title);
        Assert.Equal("CV", document.Title);
        Assert.Equal("emma-cv.pdf", document.FileName);
        Assert.Equal(4096L, document.FileSize);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(performedBy, document.UploadedBy);
        Assert.StartsWith($"{companyId}/{response.CandidateId}/", document.StorageKey);

        var application = await db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(document.Id, application.CvDocumentId);

        var upload = Assert.Single(h.Storage.Uploads);
        Assert.Equal(document.StorageKey, upload.StorageKey);
        Assert.Empty(h.Storage.Deletions);

        var intent = await db.CandidateDocumentDeletionOperations.AsNoTracking().SingleAsync();
        Assert.Equal(document.StorageKey, intent.StorageKey);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusReserved, intent.Status);
        Assert.Equal(response.CandidateId, intent.CandidateId);
        Assert.Equal(Now, intent.ConfirmedAt);
    }


    [Fact]
    public async Task HandleAsync_With_Cv_Publishes_Cv_Reference_Changed_Audit_With_Actor()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var performedBy = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, cv: FakePdf()), performedBy, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        var response = result.Result.Value!;
        var evt = Assert.Single(h.Audit.Published.OfType<ApplicationCvReferenceChangedAuditEvent>());
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(response.ApplicationId, evt.ApplicationId);
        Assert.Equal(vacancy.Id, evt.VacancyId);
        Assert.Equal(response.CandidateId, evt.CandidateId);
        Assert.Null(evt.PreviousCvDocumentId);
        Assert.Equal(response.CvDocumentId, evt.NewCvDocumentId);
        Assert.Equal(performedBy, evt.ChangedByUserId);

        Assert.Empty(h.Audit.Published.OfType<ApplicationSourceSetAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_With_Direct_Source_Publishes_Source_Set_Audit_And_No_Cv_Audit()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, source: ApplicationSource.Direct),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(ApplicationSource.Direct, result.Result.Value!.Source);
        Assert.Null(result.Result.Value.SourceExternalRecruiterId);

        var evt = Assert.Single(h.Audit.Published.OfType<ApplicationSourceSetAuditEvent>());
        Assert.Equal(ApplicationSource.Direct, evt.Source);
        Assert.Null(evt.SourceExternalRecruiterId);
        Assert.Equal(result.Result.Value.ApplicationId, evt.ApplicationId);
        Assert.Empty(h.Audit.Published.OfType<ApplicationCvReferenceChangedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Persists_ExternalRecruiter_Source_And_Publishes_Source_Audit()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, source: ApplicationSource.ExternalRecruiter, recruiterId: recruiter.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(ApplicationSource.ExternalRecruiter, result.Result.Value!.Source);
        Assert.Equal(recruiter.Id, result.Result.Value.SourceExternalRecruiterId);

        var application = await db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiter.Id, application.SourceExternalRecruiterId);

        var evt = Assert.Single(h.Audit.Published.OfType<ApplicationSourceSetAuditEvent>());
        Assert.Equal(ApplicationSource.ExternalRecruiter, evt.Source);
        Assert.Equal(recruiter.Id, evt.SourceExternalRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Accepts_Inactive_ExternalRecruiter_As_Historical_Attribution()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        recruiter.SetActiveStatus(false, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, source: ApplicationSource.ExternalRecruiter, recruiterId: recruiter.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(recruiter.Id, result.Result.Value!.SourceExternalRecruiterId);
    }


    [Fact]
    public async Task HandleAsync_Returns_Duplicate_For_Case_Insensitive_Trimmed_Email_And_Creates_Nothing()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var existing = await SeedCandidateAsync(db, companyId, "emma.clarke@example.com", firstName: "Emma", lastName: "Clarke");

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, email: "  EMMA.Clarke@Example.COM  ", cv: FakePdf()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("conflict", result.Result.Error.Code);

        var duplicate = Assert.IsType<CreateCandidateApplicationDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal(CreateCandidateApplicationDuplicateCandidateResponse.ErrorCode, duplicate.Code);
        Assert.Equal("candidate_email_exists", duplicate.Code);
        Assert.Equal(existing.Id, duplicate.ExistingCandidateId);
        Assert.Equal("Emma", duplicate.ExistingCandidateFirstName);
        Assert.Equal("Clarke", duplicate.ExistingCandidateLastName);
        Assert.Equal("emma.clarke@example.com", duplicate.ExistingCandidateEmail);
        Assert.True(duplicate.ExistingCandidateIsActive);
        Assert.False(string.IsNullOrWhiteSpace(duplicate.Error));

        await AssertNothingCreatedAsync(h, expectedCandidates: 1);
    }

    [Fact]
    public async Task HandleAsync_Reports_Inactive_Existing_Candidate_With_IsActive_False()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var existing = await SeedCandidateAsync(db, companyId, "emma.clarke@example.com", active: false);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        var duplicate = Assert.IsType<CreateCandidateApplicationDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal(existing.Id, duplicate.ExistingCandidateId);
        Assert.False(duplicate.ExistingCandidateIsActive);
        await AssertNothingCreatedAsync(h, expectedCandidates: 1);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Prefers_Active_Match_When_Legacy_Data_Holds_Several()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        await SeedCandidateAsync(db, companyId, "emma.clarke@example.com", active: false, createdAt: Now.AddDays(-30));
        var active = await SeedCandidateAsync(db, companyId, "Emma.Clarke@example.com", active: true, createdAt: Now.AddDays(-1));

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        var duplicate = Assert.IsType<CreateCandidateApplicationDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal(active.Id, duplicate.ExistingCandidateId);
        Assert.True(duplicate.ExistingCandidateIsActive);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Prefers_Oldest_When_Several_Active_Matches()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var oldest = await SeedCandidateAsync(db, companyId, "emma.clarke@example.com", createdAt: Now.AddDays(-30));
        await SeedCandidateAsync(db, companyId, "EMMA.CLARKE@example.com", createdAt: Now.AddDays(-1));

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        var duplicate = Assert.IsType<CreateCandidateApplicationDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal(oldest.Id, duplicate.ExistingCandidateId);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Email_In_Another_Company()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var otherCompanyCandidate = await SeedCandidateAsync(db, otherCompanyId, "emma.clarke@example.com");

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result.DuplicateCandidate);
        Assert.True(result.Result.IsSuccess);
        Assert.NotEqual(otherCompanyCandidate.Id, result.Result.Value!.CandidateId);
        Assert.Equal(1, await db.Candidates.CountAsync(c => c.CompanyId == companyId));
        Assert.Equal(1, await db.Candidates.CountAsync(c => c.CompanyId == otherCompanyId));
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Treat_A_Different_Email_As_Duplicate()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        await SeedCandidateAsync(db, companyId, "emma.clarke@example.com");

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, email: "emma.clarke2@example.com"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result.DuplicateCandidate);
        Assert.True(result.Result.IsSuccess);
        Assert.Equal(2, await db.Candidates.CountAsync());
    }


    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing_And_Creates_Nothing()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, Guid.NewGuid(), cv: FakePdf()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result.DuplicateCandidate);
        Assert.True(result.Result.IsFailure);
        Assert.Equal("not_found", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedVacancyWithStagesAsync(db, companyId);
        var (otherVacancy, _) = await SeedVacancyWithStagesAsync(db, otherCompanyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, otherVacancy.Id, cv: FakePdf()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("not_found", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ExternalRecruiter_Unknown()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf(), source: ApplicationSource.ExternalRecruiter, recruiterId: Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("not_found", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ExternalRecruiter_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), Guid.NewGuid(), "Other Co Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, source: ApplicationSource.ExternalRecruiter, recruiterId: recruiter.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("not_found", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }


    [Fact]
    public async Task HandleAsync_Returns_Validation_For_Disallowed_File_Type_And_Creates_Nothing()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakeFile("notes.txt", "text/plain", 512)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result.DuplicateCandidate);
        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Allowed_But_Content_Type_Is_Not()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakeFile("emma-cv.pdf", "text/plain", 512)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_For_Empty_File_And_Creates_Nothing()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf(size: 0)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Accepts_Cv_Exactly_At_Max_File_Size()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db, new CandidateDocumentUploadOptions { MaxFileSizeBytes = 100 });
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf(size: 100)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.NotNull(result.Result.Value!.CvDocumentId);
        Assert.Single(h.Storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Cv_Exceeds_Max_File_Size_By_One_Byte()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db, new CandidateDocumentUploadOptions { MaxFileSizeBytes = 100 });
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf(size: 101)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }


    [Fact]
    public async Task HandleAsync_Returns_Validation_When_No_Active_NonTerminal_Stage_And_Creates_Nothing()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        db.RecruitmentStages.AddRange(
            RecruitmentStage.Create(Guid.NewGuid(), companyId, "Hired", 1, true, RecruitmentStageTerminalOutcome.Hired, Now),
            RecruitmentStage.Create(Guid.NewGuid(), companyId, "Rejected", 2, true, RecruitmentStageTerminalOutcome.Rejected, Now));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, cv: FakePdf()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_All_NonTerminal_Stages_Are_Inactive()
    {
        await using var db = BuildContext();
        var h = BuildHarness(db);
        var companyId = Guid.NewGuid();
        var (vacancy, _) = await SeedVacancyWithStagesAsync(db, companyId);
        foreach (var stage in await db.RecruitmentStages.Where(s => s.CompanyId == companyId && !s.IsTerminal).ToListAsync())
            stage.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Result.IsFailure);
        Assert.Equal("validation", result.Result.Error.Code);
        await AssertNothingCreatedAsync(h);
    }
}
