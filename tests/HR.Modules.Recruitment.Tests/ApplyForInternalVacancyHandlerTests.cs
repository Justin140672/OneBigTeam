using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApplyForInternalVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 4: handler-level coverage (EF InMemory — the handler skips its
/// transaction and advisory locks when the provider is not relational) of an employee applying for an
/// internally advertised vacancy. Genuine concurrent races and the unique indexes are covered against
/// PostgreSQL in <see cref="ApplyForInternalVacancyConcurrencyTests"/>.
/// </summary>
public class ApplyForInternalVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required RecruitmentDbContext Db { get; init; }
        public required FakeCandidateDocumentStorageService Storage { get; init; }
        public required FakeAuditPublisher Audit { get; init; }
        public required FakeEmployeeApplicantReader Reader { get; init; }
        public required ApplyForInternalVacancyHandler Handler { get; init; }
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Harness BuildHarness(
        RecruitmentDbContext db,
        FakeEmployeeApplicantReader reader,
        CandidateDocumentUploadOptions? options = null)
    {
        var storage = new FakeCandidateDocumentStorageService();
        var audit = new FakeAuditPublisher();
        var handler = new ApplyForInternalVacancyHandler(
            db,
            reader,
            storage,
            Options.Create(options ?? new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            audit,
            new RecruitmentStageSeeder(db),
            NullLogger<ApplyForInternalVacancyHandler>.Instance);

        return new Harness { Db = db, Storage = storage, Audit = audit, Reader = reader, Handler = handler };
    }

    private static IFormFile FakeFile(string fileName, string contentType, int size) =>
        new FormFile(new MemoryStream(new byte[size]), 0, size, "CvFile", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static IFormFile FakePdf(string fileName = "priya-cv.pdf", int size = 2048) =>
        FakeFile(fileName, "application/pdf", size);

    private static ApplyForInternalVacancyRequest Request(Guid companyId, Guid vacancyId, IFormFile? cv = null, bool withCv = true) =>
        new()
        {
            CompanyId = companyId,
            VacancyId = vacancyId,
            CvFile    = withCv ? cv ?? FakePdf() : null,
        };

    private static Vacancy OpenAdvertised(Guid companyId, string title = "Senior Software Engineer")
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, null, Guid.NewGuid(), Now.AddDays(-5),
            assignedRecruiterId: null, isAdvertisedInternally: true);
        vacancy.Open(Now.AddDays(-5), DateOnly.FromDateTime(Now.AddDays(-5).UtcDateTime));
        return vacancy;
    }

    private static async Task<(Vacancy Vacancy, RecruitmentStageTestData.SeededStages Stages)> SeedCompanyAsync(
        RecruitmentDbContext db, Guid companyId)
    {
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-10));
        var vacancy = OpenAdvertised(companyId);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return (vacancy, stages);
    }

    private static async Task<Vacancy> AddVacancyAsync(RecruitmentDbContext db, Vacancy vacancy)
    {
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy;
    }

    private static async Task AssertNothingCreatedAsync(Harness h, int expectedCandidates = 0, int expectedApplications = 0, int expectedDocuments = 0)
    {
        Assert.Equal(expectedCandidates, await h.Db.Candidates.CountAsync());
        Assert.Equal(expectedApplications, await h.Db.Applications.CountAsync());
        Assert.Equal(expectedDocuments, await h.Db.CandidateDocuments.CountAsync());
        Assert.Empty(h.Storage.Uploads);
        Assert.Empty(h.Audit.Published);
    }

    private static void AssertRejected(ApplyForInternalVacancyResult result, int statusCode, string code)
    {
        Assert.True(result.Result.IsFailure);
        var rejection = Assert.IsType<ApplyForInternalVacancyRejection>(result.Rejection);
        Assert.Equal(statusCode, rejection.StatusCode);
        Assert.Equal(code, rejection.Code);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Error));
        Assert.Equal(code, result.Result.Error.Code);
    }

    private static void AssertFailed(ApplyForInternalVacancyResult result, string errorCode)
    {
        Assert.Null(result.Rejection);
        Assert.True(result.Result.IsFailure);
        Assert.Equal(errorCode, result.Result.Error.Code);
    }

    // ----- Happy path -----

    [Fact]
    public async Task HandleAsync_Creates_Linked_Candidate_Internal_Application_And_Cv_From_Employee_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, firstName: " Priya ", lastName: "Shah ", workEmail: " priya.shah@acme.example ", phoneNumber: "07700 900456")));
        var (vacancy, stages) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, FakePdf("priya-cv.pdf", 4096)), employeeId, CancellationToken.None);

        Assert.Null(result.Rejection);
        Assert.True(result.Result.IsSuccess);
        var response = result.Result.Value!;
        Assert.Equal(companyId, response.CompanyId);
        Assert.Equal(vacancy.Id, response.VacancyId);
        Assert.Equal(ApplicationSource.Internal, response.Source);
        Assert.Equal(Now, response.AppliedAt);

        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(response.CandidateId, candidate.Id);
        Assert.Equal(companyId, candidate.CompanyId);
        Assert.Equal(employeeId, candidate.EmployeeId);
        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("Shah", candidate.LastName);
        Assert.Equal("priya.shah@acme.example", candidate.Email);
        Assert.Equal("07700 900456", candidate.Phone);
        Assert.True(candidate.IsActive);

        var application = await db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(response.ApplicationId, application.Id);
        Assert.Equal(candidate.Id, application.CandidateId);
        Assert.Equal(vacancy.Id, application.VacancyId);
        Assert.Equal(ApplicationSource.Internal, application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
        Assert.Equal(stages.ApplicationReceived.Id, application.CurrentStageId);
        Assert.Equal(response.CvDocumentId, application.CvDocumentId);

        var document = await db.CandidateDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(response.CvDocumentId, document.Id);
        Assert.Equal(candidate.Id, document.CandidateId);
        Assert.Equal(companyId, document.CompanyId);
        Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
        Assert.Equal("CV", document.Title);
        Assert.Equal("priya-cv.pdf", document.FileName);
        Assert.Equal(4096L, document.FileSize);
        Assert.Equal(employeeId, document.UploadedBy);
        Assert.StartsWith($"{companyId}/{candidate.Id}/", document.StorageKey);

        var intent = await db.CandidateDocumentDeletionOperations.AsNoTracking().SingleAsync();
        Assert.Equal(document.StorageKey, intent.StorageKey);
        Assert.Equal(candidate.Id, intent.CandidateId);
        Assert.Equal(Now, intent.ConfirmedAt);

        var upload = Assert.Single(h.Storage.Uploads);
        Assert.Equal(document.StorageKey, upload.StorageKey);
        Assert.Empty(h.Storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Internal_Submitted_And_Cv_Reference_Audits_With_Employee_As_Actor()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        var response = result.Result.Value!;

        var submitted = Assert.Single(h.Audit.Published.OfType<InternalApplicationSubmittedAuditEvent>());
        Assert.Equal(companyId, submitted.CompanyId);
        Assert.Equal(response.ApplicationId, submitted.ApplicationId);
        Assert.Equal(vacancy.Id, submitted.VacancyId);
        Assert.Equal(response.CandidateId, submitted.CandidateId);
        Assert.Equal(employeeId, submitted.ApplicantEmployeeId);
        Assert.Equal(response.CvDocumentId, submitted.CvDocumentId);
        Assert.True(submitted.CandidateCreated);
        Assert.False(submitted.CandidateIdentityRefreshed);
        Assert.False(submitted.CandidateReactivated);

        IAuditEvent submittedEvent = submitted;
        Assert.Equal("application.internal_submitted", submittedEvent.EventType);
        Assert.Equal(employeeId, submittedEvent.ActorUserId);
        Assert.Equal(employeeId, submittedEvent.ActorEmployeeId);
        Assert.Equal(employeeId, submittedEvent.EmployeeId);
        Assert.Equal(response.ApplicationId, submittedEvent.EntityId);

        var cvChanged = Assert.Single(h.Audit.Published.OfType<ApplicationCvReferenceChangedAuditEvent>());
        Assert.Equal(response.ApplicationId, cvChanged.ApplicationId);
        Assert.Null(cvChanged.PreviousCvDocumentId);
        Assert.Equal(response.CvDocumentId, cvChanged.NewCvDocumentId);
        Assert.Equal(employeeId, cvChanged.ChangedByUserId);

        // A brand-new candidate is neither "updated" nor "reactivated".
        Assert.Empty(h.Audit.Published.OfType<CandidateUpdatedAuditEvent>());
        Assert.Empty(h.Audit.Published.OfType<CandidateReactivatedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Drops_Employee_Phone_Longer_Than_Candidate_Column()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, phoneNumber: new string('7', Candidate.PhoneMaxLength + 1))));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Null((await db.Candidates.AsNoTracking().SingleAsync()).Phone);
    }

    [Fact]
    public async Task HandleAsync_Seeds_Default_Stages_When_Company_Has_None_Yet()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var vacancy = await AddVacancyAsync(db, OpenAdvertised(companyId));

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
        var firstStage = await db.RecruitmentStages
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .FirstAsync();
        Assert.Equal(firstStage.Id, (await db.Applications.AsNoTracking().SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Places_Application_On_First_Active_NonTerminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, stages) = await SeedCompanyAsync(db, companyId);
        (await db.RecruitmentStages.SingleAsync(s => s.Id == stages.ApplicationReceived.Id)).SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(stages.CvReview.Id, (await db.Applications.AsNoTracking().SingleAsync()).CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_No_Active_NonTerminal_Stage_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);
        foreach (var stage in await db.RecruitmentStages.Where(s => s.CompanyId == companyId && !s.IsTerminal).ToListAsync())
            stage.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }

    // ----- The applicant is the authenticated employee, not the request -----

    [Fact]
    public async Task HandleAsync_Takes_Candidate_Identity_Only_From_Employee_Reader_For_The_Supplied_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        var reader = new FakeEmployeeApplicantReader(
            FakeEmployeeApplicantReader.Profile(companyId, applicantId, "Priya", "Shah", "priya.shah@acme.example"),
            FakeEmployeeApplicantReader.Profile(companyId, colleagueId, "Tom", "Baker", "tom.baker@acme.example"));
        var h = BuildHarness(db, reader);
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        // The request type carries no identity at all — only route ids and the file.
        var identityProperties = typeof(ApplyForInternalVacancyRequest).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "CompanyId", "CvFile", "VacancyId" }, identityProperties);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), applicantId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(applicantId, candidate.EmployeeId);
        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("Shah", candidate.LastName);
        Assert.Equal("priya.shah@acme.example", candidate.Email);
    }

    // ----- Eligibility -----

    // EmployeeApplicantEmploymentState is public, but the theory rows use names to keep the test data
    // readable and to mirror the pattern used for internal enums elsewhere in this project.
    [Theory]
    [InlineData(nameof(EmployeeApplicantEmploymentState.Draft))]
    [InlineData(nameof(EmployeeApplicantEmploymentState.Suspended))]
    [InlineData(nameof(EmployeeApplicantEmploymentState.Leaving))]
    [InlineData(nameof(EmployeeApplicantEmploymentState.Former))]
    public async Task HandleAsync_Refuses_Employee_Who_Is_Not_Active_And_Creates_Nothing(string stateName)
    {
        var state = Enum.Parse<EmployeeApplicantEmploymentState>(stateName);
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId, state: state)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status403Forbidden, ApplyForInternalVacancyRejection.NotEligibleCode);
        Assert.Equal("not_eligible_to_apply", result.Rejection!.Code);
        await AssertNothingCreatedAsync(h);
        Assert.Equal(0, await db.CandidateDocumentDeletionOperations.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Refuses_Unknown_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader());
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), Guid.NewGuid(), CancellationToken.None);

        AssertRejected(result, StatusCodes.Status403Forbidden, ApplyForInternalVacancyRejection.NotEligibleCode);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Refuses_Employee_Of_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(otherCompanyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status403Forbidden, ApplyForInternalVacancyRejection.NotEligibleCode);
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Checks_Eligibility_Before_Vacancy_So_Ineligible_Callers_Learn_Nothing_About_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, state: EmployeeApplicantEmploymentState.Former)));
        await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, Guid.NewGuid()), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status403Forbidden, ApplyForInternalVacancyRejection.NotEligibleCode);
    }

    // ----- Vacancy visibility -----

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Draft_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var draft = await AddVacancyAsync(db, Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Draft", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true));

        var result = await h.Handler.HandleAsync(Request(companyId, draft.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Closed_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var closed = OpenAdvertised(companyId, "Closed");
        closed.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        await AddVacancyAsync(db, closed);

        var result = await h.Handler.HandleAsync(Request(companyId, closed.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_OnHold_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var onHold = OpenAdvertised(companyId, "On Hold");
        onHold.Hold(Now);
        await AddVacancyAsync(db, onHold);

        var result = await h.Handler.HandleAsync(Request(companyId, onHold.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Cancelled_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var cancelled = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Cancelled", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        cancelled.Cancel(Now);
        await AddVacancyAsync(db, cancelled);

        var result = await h.Handler.HandleAsync(Request(companyId, cancelled.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Open_Vacancy_Not_Advertised_Internally()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var notAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "External Only", null, Guid.NewGuid(), Now);
        notAdvertised.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        await AddVacancyAsync(db, notAdvertised);

        var result = await h.Handler.HandleAsync(Request(companyId, notAdvertised.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Vacancy_Of_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);
        var (otherVacancy, _) = await SeedCompanyAsync(db, otherCompanyId);

        var result = await h.Handler.HandleAsync(Request(companyId, otherVacancy.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, Guid.NewGuid()), employeeId, CancellationToken.None);

        AssertFailed(result, "not_found");
        await AssertNothingCreatedAsync(h);
    }

    // ----- Duplicate prevention -----

    [Fact]
    public async Task HandleAsync_Second_Application_To_Same_Vacancy_Is_Already_Applied_Without_Upload()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var first = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);
        Assert.True(first.Result.IsSuccess);
        var auditCountAfterFirst = h.Audit.Published.Count;

        var second = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(second, StatusCodes.Status409Conflict, ApplyForInternalVacancyRejection.AlreadyAppliedCode);
        Assert.Equal("already_applied", second.Rejection!.Code);
        Assert.Equal(1, await db.Candidates.CountAsync());
        Assert.Equal(1, await db.Applications.CountAsync());
        Assert.Equal(1, await db.CandidateDocuments.CountAsync());
        Assert.Single(h.Storage.Uploads);
        Assert.Empty(h.Storage.Deletions);
        Assert.Equal(auditCountAfterFirst, h.Audit.Published.Count);
    }

    [Fact]
    public async Task HandleAsync_Treats_Withdrawn_Application_As_Already_Applied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, stages) = await SeedCompanyAsync(db, companyId);
        var linked = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", "07700 900456", Now.AddDays(-3));
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, linked.Id, stages.ApplicationReceived.Id, null, Now.AddDays(-3), ApplicationSource.Internal);
        withdrawn.Withdraw(Now.AddDays(-1));
        db.Candidates.Add(linked);
        db.Applications.Add(withdrawn);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status409Conflict, ApplyForInternalVacancyRejection.AlreadyAppliedCode);
        await AssertNothingCreatedAsync(h, expectedCandidates: 1, expectedApplications: 1);
    }

    [Fact]
    public async Task HandleAsync_Treats_Recruiter_Entered_Application_For_Linked_Candidate_As_Already_Applied()
    {
        // A hired external candidate (linked to the employee on hire) whose original application was to
        // this same vacancy — the employee has "already applied".
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, stages) = await SeedCompanyAsync(db, companyId);
        var hired = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", "priya.personal@example.com", null, null, Now.AddDays(-60));
        hired.LinkToEmployee(employeeId, Now.AddDays(-30));
        db.Candidates.Add(hired);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, hired.Id, stages.Hired.Id, null, Now.AddDays(-60), ApplicationSource.JobBoard));
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status409Conflict, ApplyForInternalVacancyRejection.AlreadyAppliedCode);
        await AssertNothingCreatedAsync(h, expectedCandidates: 1, expectedApplications: 1);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Treat_Another_Employees_Application_As_Already_Applied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(
            FakeEmployeeApplicantReader.Profile(companyId, employeeA, "Priya", "Shah", "priya.shah@acme.example"),
            FakeEmployeeApplicantReader.Profile(companyId, employeeB, "Tom", "Baker", "tom.baker@acme.example")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var a = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeA, CancellationToken.None);
        var b = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeB, CancellationToken.None);

        Assert.True(a.Result.IsSuccess);
        Assert.True(b.Result.IsSuccess);
        Assert.NotEqual(a.Result.Value!.CandidateId, b.Result.Value!.CandidateId);
        Assert.Equal(2, await db.Candidates.CountAsync());
        Assert.Equal(2, await db.Applications.CountAsync(x => x.VacancyId == vacancy.Id));
    }

    // ----- Candidate reuse -----

    [Fact]
    public async Task HandleAsync_Second_Vacancy_Reuses_Candidate_And_Refreshes_Changed_Identity()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reader = new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", "07700 900456"));
        var h = BuildHarness(db, reader);
        var (vacancy1, _) = await SeedCompanyAsync(db, companyId);
        var vacancy2 = await AddVacancyAsync(db, OpenAdvertised(companyId, "Engineering Manager"));

        var first = await h.Handler.HandleAsync(Request(companyId, vacancy1.Id), employeeId, CancellationToken.None);
        Assert.True(first.Result.IsSuccess);
        var versionAfterFirst = (await db.Candidates.AsNoTracking().SingleAsync()).Version;

        // The Employee record changed between the two applications (e.g. a name change).
        reader.Set(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, "Priya", "Shah-Patel", "priya.shah-patel@acme.example", "07700 900999"));

        var second = await h.Handler.HandleAsync(Request(companyId, vacancy2.Id, FakePdf("priya-cv-v2.pdf")), employeeId, CancellationToken.None);

        Assert.True(second.Result.IsSuccess);
        Assert.Equal(first.Result.Value!.CandidateId, second.Result.Value!.CandidateId);

        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(employeeId, candidate.EmployeeId);
        Assert.Equal("Shah-Patel", candidate.LastName);
        Assert.Equal("priya.shah-patel@acme.example", candidate.Email);
        Assert.Equal("07700 900999", candidate.Phone);
        Assert.True(candidate.Version > versionAfterFirst);

        var applications = await db.Applications.AsNoTracking().ToListAsync();
        Assert.Equal(2, applications.Count);
        Assert.All(applications, a => Assert.Equal(candidate.Id, a.CandidateId));
        Assert.All(applications, a => Assert.Equal(ApplicationSource.Internal, a.Source));
        Assert.Equal(new[] { vacancy1.Id, vacancy2.Id }.OrderBy(x => x), applications.Select(a => a.VacancyId).OrderBy(x => x));

        var documents = await db.CandidateDocuments.AsNoTracking().ToListAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal(candidate.Id, d.CandidateId));
        Assert.Equal(documents.Select(d => d.Id).OrderBy(x => x), applications.Select(a => a.CvDocumentId!.Value).OrderBy(x => x));
        Assert.Equal(2, h.Storage.Uploads.Count);

        var updated = Assert.Single(h.Audit.Published.OfType<CandidateUpdatedAuditEvent>());
        Assert.Equal(candidate.Id, updated.CandidateId);
        Assert.Equal("Shah", updated.Before.LastName);
        Assert.Equal("priya.shah@acme.example", updated.Before.Email);
        Assert.Equal("Shah-Patel", updated.After.LastName);
        Assert.Equal("priya.shah-patel@acme.example", updated.After.Email);

        var submitted = h.Audit.Published.OfType<InternalApplicationSubmittedAuditEvent>().ToList();
        Assert.Equal(2, submitted.Count);
        var secondSubmitted = Assert.Single(submitted, e => e.VacancyId == vacancy2.Id);
        Assert.False(secondSubmitted.CandidateCreated);
        Assert.True(secondSubmitted.CandidateIdentityRefreshed);
        Assert.False(secondSubmitted.CandidateReactivated);
    }

    [Fact]
    public async Task HandleAsync_Second_Vacancy_With_Unchanged_Identity_Publishes_No_Candidate_Updated_Audit()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy1, _) = await SeedCompanyAsync(db, companyId);
        var vacancy2 = await AddVacancyAsync(db, OpenAdvertised(companyId, "Engineering Manager"));

        await h.Handler.HandleAsync(Request(companyId, vacancy1.Id), employeeId, CancellationToken.None);
        var versionAfterFirst = (await db.Candidates.AsNoTracking().SingleAsync()).Version;
        var second = await h.Handler.HandleAsync(Request(companyId, vacancy2.Id), employeeId, CancellationToken.None);

        Assert.True(second.Result.IsSuccess);
        Assert.Empty(h.Audit.Published.OfType<CandidateUpdatedAuditEvent>());
        Assert.Equal(versionAfterFirst, (await db.Candidates.AsNoTracking().SingleAsync()).Version);
        var secondSubmitted = Assert.Single(h.Audit.Published.OfType<InternalApplicationSubmittedAuditEvent>(), e => e.VacancyId == vacancy2.Id);
        Assert.False(secondSubmitted.CandidateCreated);
        Assert.False(secondSubmitted.CandidateIdentityRefreshed);
    }

    [Fact]
    public async Task HandleAsync_Reuses_Hired_External_Candidate_Linked_To_The_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example")));
        var (vacancy, stages) = await SeedCompanyAsync(db, companyId);

        // Originally an external candidate (personal email), linked to the employee when hired.
        var hireVacancy = await AddVacancyAsync(db, OpenAdvertised(companyId, "Original Role"));
        var hired = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", "priya.personal@example.com", null, "https://example.com/old-cv.pdf", Now.AddDays(-400));
        hired.LinkToEmployee(employeeId, Now.AddDays(-365));
        db.Candidates.Add(hired);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, hireVacancy.Id, hired.Id, stages.Hired.Id, null, Now.AddDays(-400), ApplicationSource.JobBoard));
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(hired.Id, result.Result.Value!.CandidateId);

        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(hired.Id, candidate.Id);
        Assert.Equal("priya.shah@acme.example", candidate.Email);
        Assert.Equal("https://example.com/old-cv.pdf", candidate.ResumeUrl);
        Assert.Equal(2, await db.Applications.CountAsync(a => a.CandidateId == hired.Id));

        var submitted = Assert.Single(h.Audit.Published.OfType<InternalApplicationSubmittedAuditEvent>());
        Assert.False(submitted.CandidateCreated);
        Assert.True(submitted.CandidateIdentityRefreshed);
        Assert.Single(h.Audit.Published.OfType<CandidateUpdatedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Reactivates_Inactive_Linked_Candidate_And_Audits_Employee_As_Actor()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", "07700 900456")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        // A previously hired external candidate that a recruiter deactivated after the hire.
        var linked = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", "priya.shah@acme.example", "07700 900456", null, Now.AddDays(-400));
        linked.LinkToEmployee(employeeId, Now.AddDays(-365));
        linked.Deactivate(Guid.NewGuid(), "Hired", Now.AddDays(-360));
        db.Candidates.Add(linked);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(linked.Id, result.Result.Value!.CandidateId);

        var candidate = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.True(candidate.IsActive);
        Assert.Equal(employeeId, candidate.ReactivatedByUserId);
        Assert.Equal(Now, candidate.ReactivatedAt);
        Assert.True(candidate.Version > 1);

        var reactivated = Assert.Single(h.Audit.Published.OfType<CandidateReactivatedAuditEvent>());
        Assert.Equal(linked.Id, reactivated.CandidateId);
        Assert.Equal(employeeId, reactivated.ReactivatedByUserId);
        Assert.Equal(employeeId, ((IAuditEvent)reactivated).ActorUserId);

        // Identity was unchanged, so no candidate.updated.
        Assert.Empty(h.Audit.Published.OfType<CandidateUpdatedAuditEvent>());
        var submitted = Assert.Single(h.Audit.Published.OfType<InternalApplicationSubmittedAuditEvent>());
        Assert.True(submitted.CandidateReactivated);
        Assert.False(submitted.CandidateCreated);
    }

    [Fact]
    public async Task HandleAsync_Refuses_Purged_Linked_Candidate_With_Conflict_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);
        var linked = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now.AddDays(-400));
        linked.Purge(Guid.NewGuid(), Now.AddDays(-1));
        db.Candidates.Add(linked);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "conflict");
        await AssertNothingCreatedAsync(h, expectedCandidates: 1);
        Assert.Equal("[purged]", (await db.Candidates.AsNoTracking().SingleAsync()).FirstName);
    }

    // ----- Email owned by a different candidate -----

    [Fact]
    public async Task HandleAsync_Refuses_When_Work_Email_Belongs_To_Unlinked_External_Candidate_Case_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, workEmail: "priya.shah@acme.example")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);
        var external = Candidate.Create(Guid.NewGuid(), companyId, "P", "Shah", "  PRIYA.Shah@Acme.EXAMPLE ", "0111", null, Now.AddDays(-100));
        db.Candidates.Add(external);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status409Conflict, ApplyForInternalVacancyRejection.EmailInUseCode);
        Assert.Equal("applicant_email_in_use", result.Rejection!.Code);
        await AssertNothingCreatedAsync(h, expectedCandidates: 1);
        Assert.Equal(0, await db.CandidateDocumentDeletionOperations.CountAsync());

        var untouched = await db.Candidates.AsNoTracking().SingleAsync();
        Assert.Equal(external.Id, untouched.Id);
        Assert.Null(untouched.EmployeeId);
        Assert.Equal("P", untouched.FirstName);
        Assert.Equal("0111", untouched.Phone);
        Assert.Equal(1, untouched.Version);
    }

    [Fact]
    public async Task HandleAsync_Refuses_When_Changed_Work_Email_Of_Linked_Candidate_Belongs_To_Another_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, workEmail: "priya.new@acme.example")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);
        var linked = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.old@acme.example", null, Now.AddDays(-30));
        var external = Candidate.Create(Guid.NewGuid(), companyId, "Other", "Person", "priya.new@acme.example", null, null, Now.AddDays(-20));
        db.Candidates.AddRange(linked, external);
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertRejected(result, StatusCodes.Status409Conflict, ApplyForInternalVacancyRejection.EmailInUseCode);
        await AssertNothingCreatedAsync(h, expectedCandidates: 2);
        Assert.Equal("priya.old@acme.example", (await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == linked.Id)).Email);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Same_Email_Candidate_In_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
            companyId, employeeId, workEmail: "priya.shah@acme.example")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);
        db.Candidates.Add(Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Priya", "Shah", "priya.shah@acme.example", null, null, Now.AddDays(-10)));
        await db.SaveChangesAsync();

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(1, await db.Candidates.CountAsync(c => c.CompanyId == companyId));
    }

    // ----- Employee identity / CV file validation -----

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Employee_Has_No_Work_Email()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId, workEmail: "   ")));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_CvFile_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, withCv: false), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_For_Disallowed_File_Type_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, FakeFile("notes.txt", "text/plain", 512)), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
        Assert.Equal(0, await db.CandidateDocumentDeletionOperations.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Allowed_But_Content_Type_Is_Not()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(
            Request(companyId, vacancy.Id, FakeFile("priya-cv.pdf", "text/plain", 512)), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_For_Empty_File()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)));
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, FakePdf(size: 0)), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }

    [Fact]
    public async Task HandleAsync_Accepts_Cv_Exactly_At_Max_File_Size()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)),
            new CandidateDocumentUploadOptions { MaxFileSizeBytes = 100 });
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, FakePdf(size: 100)), employeeId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Single(h.Storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Cv_Exceeds_Max_File_Size_By_One_Byte()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var h = BuildHarness(db, new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(companyId, employeeId)),
            new CandidateDocumentUploadOptions { MaxFileSizeBytes = 100 });
        var (vacancy, _) = await SeedCompanyAsync(db, companyId);

        var result = await h.Handler.HandleAsync(Request(companyId, vacancy.Id, FakePdf(size: 101)), employeeId, CancellationToken.None);

        AssertFailed(result, "validation");
        await AssertNothingCreatedAsync(h);
    }
}
