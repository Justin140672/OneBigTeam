using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 4: POST /api/companies/{c}/internal-vacancies/{v}/applications — the
/// signed-in employee applies for an internally advertised vacancy with a CV (multipart/form-data,
/// field <c>CvFile</c>). See ApplyForInternalVacancyHandlerTests / ApplyForInternalVacancyValidatorTests /
/// ApplyForInternalVacancyConcurrencyTests in HR.Modules.Recruitment.Tests for unit-level and race
/// coverage.
///
/// Every applicant here is a plain employee (ONLY SystemRoles.Employee — no recruitment permission)
/// whose Employee row is seeded directly with Id == the test user id (the UserId == EmployeeId
/// convention the endpoint relies on). Each test uses its own fixed user id, because Employee.Id is a
/// global primary key and each test needs its own company.
///
/// Status codes: FluentValidation failures surface as 422 (FastEndpoints is configured with
/// <c>Errors.StatusCode = 422</c> in HR.Api — see CreateApplicationSourceEndpointTests /
/// CreateCandidateApplicationEndpointTests); handler validation errors (bad CV file) are 400 with code
/// "validation"; coded refusals are <c>{ error, code }</c> with 403/409.
/// </summary>
[Collection("Integration")]
public class ApplyForInternalVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HappyPathUser        = new("cc00cf50-0000-0000-0000-000000000001");
    private static readonly Guid AppliesTwiceUser     = new("cc00cf50-0000-0000-0000-000000000002");
    private static readonly Guid TwoVacanciesUser     = new("cc00cf50-0000-0000-0000-000000000003");
    private static readonly Guid CrossCompanyUser     = new("cc00cf50-0000-0000-0000-000000000004");
    private static readonly Guid ForeignVacancyUser   = new("cc00cf50-0000-0000-0000-000000000005");
    private static readonly Guid HiddenVacanciesUser  = new("cc00cf50-0000-0000-0000-000000000006");
    private static readonly Guid MissingCvUser        = new("cc00cf50-0000-0000-0000-000000000007");
    private static readonly Guid BadFileUser          = new("cc00cf50-0000-0000-0000-000000000008");
    private static readonly Guid LeavingUser          = new("cc00cf50-0000-0000-0000-000000000009");
    private static readonly Guid DraftUser            = new("cc00cf50-0000-0000-0000-00000000000a");
    private static readonly Guid NoEmployeeRowUser    = new("cc00cf50-0000-0000-0000-00000000000b");
    private static readonly Guid EmailInUseUser       = new("cc00cf50-0000-0000-0000-00000000000c");
    private static readonly Guid HasAppliedUser       = new("cc00cf50-0000-0000-0000-00000000000d");
    private static readonly Guid SuspendedUser        = new("cc00cf50-0000-0000-0000-00000000000e");
    private static readonly Guid FormerUser           = new("cc00cf50-0000-0000-0000-00000000000f");
    private static readonly Guid RecruiterUser        = new("cc00cf50-0000-0000-0000-000000000010");

    private static readonly Guid[] PlainEmployees =
    [
        HappyPathUser, AppliesTwiceUser, TwoVacanciesUser, CrossCompanyUser, ForeignVacancyUser,
        HiddenVacanciesUser, MissingCvUser, BadFileUser, LeavingUser, DraftUser, NoEmployeeRowUser,
        EmailInUseUser, HasAppliedUser, SuspendedUser, FormerUser,
    ];

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ApplyForInternalVacancyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            foreach (var user in PlainEmployees)
                await TestRoleSeeder.AssignRoleAsync(factory, user, SystemRoles.Employee);

            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static string ApplyUrl(Guid companyId, Guid vacancyId) =>
        $"/api/companies/{companyId}/internal-vacancies/{vacancyId}/applications";

    private static string UniqueWorkEmail(string prefix = "priya") => $"{prefix}.{Guid.NewGuid():N}@acme.example";

    private static MultipartFormDataContent BuildForm((string FileName, string ContentType, byte[] Bytes)? cv)
    {
        var content = new MultipartFormDataContent();
        if (cv is { } file)
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(fileContent, "CvFile", file.FileName);
        }

        return content;
    }

    private static (string, string, byte[]) PdfCv(string fileName = "priya-cv.pdf")
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46; // %PDF
        return (fileName, "application/pdf", bytes);
    }

    /// <summary>
    /// Seeds the applying employee's Employee row directly (Id == the test user id), with the given
    /// status. Employee.Create leaves an employee in Draft; Active goes through the real Activate()
    /// domain method, the other states through the reflection test helper.
    /// </summary>
    private async Task SeedEmployeeAsync(
        Guid userId,
        Guid companyId,
        string workEmail,
        EmploymentStatus status = EmploymentStatus.Active,
        string firstName = "Priya",
        string lastName = "Shah")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(
            userId, companyId, firstName, lastName, workEmail,
            new DateOnly(2024, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);

        if (status == EmploymentStatus.Active)
            employee.Activate(now);
        else if (status != EmploymentStatus.Draft)
            employee.SetStatusForTesting(status, now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedVacancyAsync(
        Guid companyId, string advertTitle, bool advertisedInternally = true, bool open = true, bool close = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), advertTitle, "A description", Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: advertisedInternally);
        if (open)
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        if (close)
            vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    private async Task<int> CountApplicationsAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.CountAsync(a => a.CompanyId == companyId);
    }

    private async Task<int> CountCandidatesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Candidates.CountAsync(c => c.CompanyId == companyId);
    }

    private async Task AssertNothingCreatedAsync(Guid companyId, int expectedCandidates = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(expectedCandidates, await db.Candidates.CountAsync(c => c.CompanyId == companyId));
        Assert.Equal(0, await db.Applications.CountAsync(a => a.CompanyId == companyId));
        Assert.Equal(0, await db.CandidateDocuments.CountAsync(d => d.CompanyId == companyId));
    }

    private static async Task AssertCodedRefusalAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(body);
        Assert.Equal(code, body!.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Error));
    }

    // ---- Authentication ---------------------------------------------------------------------------

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(ApplyUrl(Guid.NewGuid(), Guid.NewGuid()), BuildForm(PdfCv()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Happy path -------------------------------------------------------------------------------

    [Fact]
    public async Task Post_By_Plain_Employee_Creates_Linked_Candidate_Internal_Application_Cv_And_Audit()
    {
        var companyId = Guid.NewGuid();
        var workEmail = UniqueWorkEmail();
        await SeedEmployeeAsync(HappyPathUser, companyId, workEmail, firstName: "Priya", lastName: "Shah");
        var vacancyId = await SeedVacancyAsync(companyId, "Senior Software Engineer");
        using var client = await ClientAs(HappyPathUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv("priya-cv.pdf")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.NotNull(created);
        Assert.Equal(companyId, created!.CompanyId);
        Assert.Equal(vacancyId, created.VacancyId);
        Assert.Equal("Internal", created.Source);
        Assert.NotEqual(Guid.Empty, created.CvDocumentId);
        Assert.Equal($"/api/companies/{companyId}/internal-vacancies/{vacancyId}", response.Headers.Location?.OriginalString);

        // The employee-facing response carries no recruiter-facing pipeline data.
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            foreach (var forbidden in new[] { "currentStageId", "notes", "firstName", "lastName", "email" })
                Assert.DoesNotContain(doc.RootElement.EnumerateObject(), p => string.Equals(p.Name, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.CompanyId == companyId);
        Assert.Equal(created.CandidateId, candidate.Id);
        Assert.Equal(HappyPathUser, candidate.EmployeeId);
        Assert.Equal("Priya", candidate.FirstName);
        Assert.Equal("Shah", candidate.LastName);
        Assert.Equal(workEmail, candidate.Email);
        Assert.True(candidate.IsActive);

        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.CompanyId == companyId);
        Assert.Equal(created.ApplicationId, application.Id);
        Assert.Equal(candidate.Id, application.CandidateId);
        Assert.Equal(vacancyId, application.VacancyId);
        Assert.Equal(ApplicationSource.Internal, application.Source);
        Assert.Equal(created.CvDocumentId, application.CvDocumentId);

        var document = await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.CompanyId == companyId);
        Assert.Equal(created.CvDocumentId, document.Id);
        Assert.Equal(candidate.Id, document.CandidateId);
        Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
        Assert.Equal("CV", document.Title);
        Assert.Equal("priya-cv.pdf", document.FileName);
        Assert.Equal(HappyPathUser, document.UploadedBy);

        var intent = await db.CandidateDocumentDeletionOperations.AsNoTracking()
            .SingleAsync(o => o.StorageKey == document.StorageKey);
        Assert.NotNull(intent.ConfirmedAt);

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var submitted = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "application.internal_submitted" && e.EntityId == created.ApplicationId)
            .SingleOrDefaultAsync();
        Assert.NotNull(submitted);
        Assert.Equal(HappyPathUser, submitted!.ActorUserId);
        Assert.Equal(HappyPathUser, submitted.ActorEmployeeId);
        Assert.Equal(HappyPathUser, submitted.EmployeeId);
        Assert.Contains(created.CvDocumentId.ToString(), submitted.AfterJson, StringComparison.OrdinalIgnoreCase);

        var cvChanged = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "application.cv_reference_changed" && e.EntityId == created.ApplicationId)
            .SingleOrDefaultAsync();
        Assert.NotNull(cvChanged);
        Assert.Equal(HappyPathUser, cvChanged!.ActorUserId);
    }

    // ---- Duplicates and reuse ---------------------------------------------------------------------

    [Fact]
    public async Task Post_Same_Vacancy_Twice_Returns_Conflict_Already_Applied_And_Keeps_One_Application()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(AppliesTwiceUser, companyId, UniqueWorkEmail());
        var vacancyId = await SeedVacancyAsync(companyId, "Data Analyst");
        using var client = await ClientAs(AppliesTwiceUser, companyId);

        var first = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv("second.pdf")));

        await AssertCodedRefusalAsync(second, HttpStatusCode.Conflict, "already_applied");
        Assert.Equal(1, await CountApplicationsAsync(companyId));
        Assert.Equal(1, await CountCandidatesAsync(companyId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(1, await db.CandidateDocuments.CountAsync(d => d.CompanyId == companyId));
    }

    [Fact]
    public async Task Post_Different_Vacancy_Reuses_The_Same_Linked_Candidate()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(TwoVacanciesUser, companyId, UniqueWorkEmail());
        var vacancyA = await SeedVacancyAsync(companyId, "Role A");
        var vacancyB = await SeedVacancyAsync(companyId, "Role B");
        using var client = await ClientAs(TwoVacanciesUser, companyId);

        var first = await client.PostAsync(ApplyUrl(companyId, vacancyA), BuildForm(PdfCv("a.pdf")));
        var second = await client.PostAsync(ApplyUrl(companyId, vacancyB), BuildForm(PdfCv("b.pdf")));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<CreatedPayload>();
        var b = await second.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.Equal(a!.CandidateId, b!.CandidateId);
        Assert.NotEqual(a.ApplicationId, b.ApplicationId);
        Assert.NotEqual(a.CvDocumentId, b.CvDocumentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.CompanyId == companyId);
        Assert.Equal(a.CandidateId, candidate.Id);
        Assert.Equal(TwoVacanciesUser, candidate.EmployeeId);
        Assert.Equal(2, await db.Applications.CountAsync(x => x.CompanyId == companyId && x.CandidateId == candidate.Id));
        Assert.Equal(2, await db.CandidateDocuments.CountAsync(d => d.CompanyId == companyId && d.CandidateId == candidate.Id));
    }

    // ---- Tenancy and visibility -------------------------------------------------------------------

    [Fact]
    public async Task Post_To_Another_Companys_Route_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedEmployeeAsync(CrossCompanyUser, companyId, UniqueWorkEmail());
        var otherVacancyId = await SeedVacancyAsync(otherCompanyId, "Other Company Role");
        using var client = await ClientAs(CrossCompanyUser, companyId);

        var response = await client.PostAsync(ApplyUrl(otherCompanyId, otherVacancyId), BuildForm(PdfCv()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingCreatedAsync(otherCompanyId);
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_For_Another_Companys_Vacancy_Via_Own_Route_Returns_NotFound()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedEmployeeAsync(ForeignVacancyUser, companyId, UniqueWorkEmail());
        var otherVacancyId = await SeedVacancyAsync(otherCompanyId, "Other Company Role");
        using var client = await ClientAs(ForeignVacancyUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, otherVacancyId), BuildForm(PdfCv()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingCreatedAsync(companyId);
        await AssertNothingCreatedAsync(otherCompanyId);
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Draft_Closed_And_Not_Advertised_Vacancies()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(HiddenVacanciesUser, companyId, UniqueWorkEmail());
        var draft = await SeedVacancyAsync(companyId, "Draft Role", advertisedInternally: true, open: false);
        var closed = await SeedVacancyAsync(companyId, "Closed Role", advertisedInternally: true, open: true, close: true);
        var notAdvertised = await SeedVacancyAsync(companyId, "External Only Role", advertisedInternally: false, open: true);
        using var client = await ClientAs(HiddenVacanciesUser, companyId);

        foreach (var vacancyId in new[] { draft, closed, notAdvertised })
        {
            var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        await AssertNothingCreatedAsync(companyId);
    }

    // ---- Validation --------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Without_CvFile_Returns_UnprocessableEntity_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(MissingCvUser, companyId, UniqueWorkEmail());
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(MissingCvUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(cv: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_With_Unsupported_File_Type_Returns_BadRequest_Validation_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(BadFileUser, companyId, UniqueWorkEmail());
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(BadFileUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId),
            BuildForm(("notes.txt", "text/plain", "plain text, not a CV"u8.ToArray())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.Equal("validation", body!.Code);
        await AssertNothingCreatedAsync(companyId);
    }

    // ---- Eligibility -------------------------------------------------------------------------------

    [Fact]
    public async Task Post_By_Leaving_Employee_Returns_Forbidden_Not_Eligible()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(LeavingUser, companyId, UniqueWorkEmail(), EmploymentStatus.Leaving);
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(LeavingUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Forbidden, "not_eligible_to_apply");
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_By_Draft_Employee_Returns_Forbidden_Not_Eligible()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(DraftUser, companyId, UniqueWorkEmail(), EmploymentStatus.Draft);
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(DraftUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Forbidden, "not_eligible_to_apply");
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_By_Suspended_Employee_Returns_Forbidden_Not_Eligible()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(SuspendedUser, companyId, UniqueWorkEmail(), EmploymentStatus.Suspended);
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(SuspendedUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Forbidden, "not_eligible_to_apply");
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_By_Former_Employee_Returns_Forbidden_Not_Eligible()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(FormerUser, companyId, UniqueWorkEmail(), EmploymentStatus.FormerEmployee);
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(FormerUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Forbidden, "not_eligible_to_apply");
        await AssertNothingCreatedAsync(companyId);
    }

    [Fact]
    public async Task Post_By_User_With_No_Employee_Record_In_Company_Returns_Forbidden_Not_Eligible()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        using var client = await ClientAs(NoEmployeeRowUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Forbidden, "not_eligible_to_apply");
        await AssertNothingCreatedAsync(companyId);
    }

    // ---- Email already used by an external candidate ----------------------------------------------

    [Fact]
    public async Task Post_When_Work_Email_Belongs_To_Unlinked_External_Candidate_Returns_Conflict_And_Leaves_It_Untouched()
    {
        var companyId = Guid.NewGuid();
        var workEmail = UniqueWorkEmail();
        await SeedEmployeeAsync(EmailInUseUser, companyId, workEmail);
        var vacancyId = await SeedVacancyAsync(companyId, "Role");
        var externalId = await RecruitmentTestSeeder.SeedCandidateAsync(
            _factory, companyId, Now, "External", "Person", email: workEmail.ToUpperInvariant());
        using var client = await ClientAs(EmailInUseUser, companyId);

        var response = await client.PostAsync(ApplyUrl(companyId, vacancyId), BuildForm(PdfCv()));

        await AssertCodedRefusalAsync(response, HttpStatusCode.Conflict, "applicant_email_in_use");
        await AssertNothingCreatedAsync(companyId, expectedCandidates: 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var external = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == externalId);
        Assert.Null(external.EmployeeId);
        Assert.Equal("External", external.FirstName);
        Assert.Equal(workEmail.ToUpperInvariant(), external.Email);
    }

    // ---- HasApplied on the internal vacancy list --------------------------------------------------

    [Fact]
    public async Task Get_Internal_Vacancies_Reports_HasApplied_Only_For_The_Applied_Vacancy()
    {
        var companyId = Guid.NewGuid();
        await SeedEmployeeAsync(HasAppliedUser, companyId, UniqueWorkEmail());
        var applied = await SeedVacancyAsync(companyId, "Applied Role");
        var notApplied = await SeedVacancyAsync(companyId, "Other Role");
        using var client = await ClientAs(HasAppliedUser, companyId);

        var apply = await client.PostAsync(ApplyUrl(companyId, applied), BuildForm(PdfCv()));
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetProperty("hasApplied").GetBoolean());
        Assert.Equal(2, items.Count);
        Assert.True(items[applied]);
        Assert.False(items[notApplied]);
    }

    // ---- Recruiters cannot record the Internal source ---------------------------------------------

    [Fact]
    public async Task Recruiter_CreateApplication_With_Internal_Source_Returns_UnprocessableEntity()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new { companyId, vacancyId, candidateId, source = "Internal" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountApplicationsAsync(companyId));
    }

    [Fact]
    public async Task Recruiter_CreateCandidateApplication_With_Internal_Source_Returns_UnprocessableEntity()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Emma"), "FirstName" },
            { new StringContent("Clarke"), "LastName" },
            { new StringContent($"emma.{Guid.NewGuid():N}@example.com"), "Email" },
            { new StringContent("Internal"), "Source" },
        };

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate", form);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountCandidatesAsync(companyId));
        Assert.Equal(0, await CountApplicationsAsync(companyId));
    }

    private sealed record CreatedPayload(
        Guid ApplicationId,
        Guid CompanyId,
        Guid VacancyId,
        Guid CandidateId,
        Guid CvDocumentId,
        string Source,
        DateTimeOffset AppliedAt);

    private sealed record ProblemPayload(string Error, string Code);
}
