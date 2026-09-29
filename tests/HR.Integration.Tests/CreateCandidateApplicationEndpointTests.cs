using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 3: POST /api/companies/{c}/vacancies/{v}/applications/new-candidate
/// creates a brand-new candidate, an optional CV document and their application in one
/// multipart/form-data call. See CreateCandidateApplicationHandlerTests /
/// CreateCandidateApplicationValidatorTests / CreateCandidateApplicationConcurrencyTests in
/// HR.Modules.Recruitment.Tests for unit-level and race coverage.
///
/// Status codes: FluentValidation failures surface as 422 (FastEndpoints is configured with
/// <c>Errors.StatusCode = 422</c> in HR.Api, as asserted by CreateApplicationSourceEndpointTests);
/// business validation errors from the handler (bad CV file) are 400 with code "validation".
/// </summary>
[Collection("Integration")]
public class CreateCandidateApplicationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00cf40-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc00cf40-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CreateCandidateApplicationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static string CreateUrl(Guid companyId, Guid vacancyId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate";

    private static string UniqueEmail(string prefix = "emma") => $"{prefix}.{Guid.NewGuid():N}@example.com";

    private static MultipartFormDataContent BuildForm(
        string? firstName = "Emma",
        string? lastName = "Clarke",
        string? email = null,
        string? phone = null,
        string? notes = null,
        string? source = null,
        Guid? recruiterId = null,
        (string FileName, string ContentType, byte[] Bytes)? cv = null)
    {
        var content = new MultipartFormDataContent();
        if (firstName is not null) content.Add(new StringContent(firstName), "FirstName");
        if (lastName is not null) content.Add(new StringContent(lastName), "LastName");
        if (email is not null) content.Add(new StringContent(email), "Email");
        if (phone is not null) content.Add(new StringContent(phone), "Phone");
        if (notes is not null) content.Add(new StringContent(notes), "Notes");
        if (source is not null) content.Add(new StringContent(source), "Source");
        if (recruiterId is not null) content.Add(new StringContent(recruiterId.Value.ToString()), "SourceExternalRecruiterId");

        if (cv is { } file)
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(fileContent, "CvFile", file.FileName);
        }

        return content;
    }

    private static (string, string, byte[]) PdfCv(string fileName = "emma-cv.pdf")
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
        return (fileName, "application/pdf", bytes);
    }

    private async Task<Guid> SeedRecruiterAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }

    private async Task<int> CountCandidatesWithEmailAsync(Guid companyId, string email)
    {
        var normalised = email.Trim().ToLowerInvariant();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Candidates.CountAsync(c => c.CompanyId == companyId && c.Email.ToLower() == normalised);
    }

    private async Task<int> CountApplicationsForVacancyAsync(Guid vacancyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.CountAsync(a => a.VacancyId == vacancyId);
    }

    private async Task<Guid> ExpectedInitialStageIdAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => s.Id)
            .FirstAsync();
    }


    [Fact]
    public async Task Post_Without_Cv_Returns_Created_And_Persists_Candidate_And_Application_On_Initial_Stage()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: email, phone: "07700 900123", notes: "Met at a careers fair"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.NotNull(created);
        Assert.Equal(companyId, created!.CompanyId);
        Assert.Equal(vacancyId, created.VacancyId);
        Assert.Equal("Emma", created.FirstName);
        Assert.Equal("Clarke", created.LastName);
        Assert.Equal(email, created.Email);
        Assert.Null(created.CvDocumentId);
        Assert.Null(created.Source);
        Assert.Null(created.SourceExternalRecruiterId);
        Assert.Equal($"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{created.ApplicationId}",
            response.Headers.Location?.OriginalString);

        var expectedStageId = await ExpectedInitialStageIdAsync(companyId);
        Assert.Equal(expectedStageId, created.CurrentStageId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == created.CandidateId);
        Assert.Equal(companyId, candidate.CompanyId);
        Assert.Equal(email, candidate.Email);
        Assert.Equal("07700 900123", candidate.Phone);
        Assert.True(candidate.IsActive);

        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == created.ApplicationId);
        Assert.Equal(candidate.Id, application.CandidateId);
        Assert.Equal(vacancyId, application.VacancyId);
        Assert.Equal(expectedStageId, application.CurrentStageId);
        Assert.Null(application.CvDocumentId);

        Assert.False(await db.CandidateDocuments.AnyAsync(d => d.CandidateId == candidate.Id));
    }

    [Fact]
    public async Task Post_Places_Application_On_First_Active_NonTerminal_Stage_Skipping_Inactive_Ones()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        Guid firstStageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var first = await db.RecruitmentStages
                .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
                .OrderBy(s => s.DisplayOrder)
                .FirstAsync();
            firstStageId = first.Id;
            first.SetActiveStatus(false, Now);
            await db.SaveChangesAsync();
        }

        var expectedStageId = await ExpectedInitialStageIdAsync(companyId);
        Assert.NotEqual(firstStageId, expectedStageId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, seeded.VacancyId), BuildForm(email: UniqueEmail()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.Equal(expectedStageId, created!.CurrentStageId);
    }

    [Fact]
    public async Task Post_With_Pdf_Cv_Returns_Created_With_Cv_Document_Attached_And_Listed_As_Current_Cv()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: UniqueEmail(), cv: PdfCv("emma-cv.pdf")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.NotNull(created);
        Assert.NotNull(created!.CvDocumentId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var document = await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.CandidateId == created.CandidateId);
            Assert.Equal(created.CvDocumentId, document.Id);
            Assert.Equal(companyId, document.CompanyId);
            Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
            Assert.Equal("CV", document.Title);
            Assert.Equal("emma-cv.pdf", document.FileName);
            Assert.Equal(2048L, document.FileSize);
            Assert.Equal(RecruiterUser, document.UploadedBy);

            var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == created.ApplicationId);
            Assert.Equal(document.Id, application.CvDocumentId);

            var intent = await db.CandidateDocumentDeletionOperations.AsNoTracking()
                .SingleAsync(o => o.StorageKey == document.StorageKey);
            Assert.NotNull(intent.ConfirmedAt);
        }

        var list = await client.GetAsync($"/api/companies/{companyId}/candidates/{created.CandidateId}/documents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var documents = await list.Content.ReadFromJsonAsync<DocumentListPayload>();
        var item = Assert.Single(documents!.Items);
        Assert.Equal(created.CvDocumentId, item.Id);
        Assert.Equal("Cv", item.Kind);
        Assert.True(item.IsCurrentCv);
        Assert.Equal(1, item.ReferencingApplicationCount);
    }

    [Fact]
    public async Task Post_With_ExternalRecruiter_Source_Persists_Source_And_Recruiter()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var recruiterId = await SeedRecruiterAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: UniqueEmail(), source: "ExternalRecruiter", recruiterId: recruiterId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.Equal("ExternalRecruiter", created!.Source);
        Assert.Equal(recruiterId, created.SourceExternalRecruiterId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == created.ApplicationId);
        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiterId, application.SourceExternalRecruiterId);
    }


    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(CreateUrl(Guid.NewGuid(), Guid.NewGuid()), BuildForm(email: UniqueEmail()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_For_Plain_Employee_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId), BuildForm(email: email));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));
    }


    [Fact]
    public async Task Post_Returns_Conflict_With_Existing_Candidate_When_Email_Exists_Case_Insensitively()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail("liam");
        var existingId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now, "Liam", "Turner", email);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: $"  {email.ToUpperInvariant()} ", cv: PdfCv()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DuplicatePayload>();
        Assert.NotNull(body);
        Assert.Equal("candidate_email_exists", body!.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Error));
        Assert.Equal(existingId, body.ExistingCandidateId);
        Assert.Equal("Liam", body.ExistingCandidateFirstName);
        Assert.Equal("Turner", body.ExistingCandidateLastName);
        Assert.Equal(email, body.ExistingCandidateEmail);
        Assert.True(body.ExistingCandidateIsActive);

        Assert.Equal(1, await CountCandidatesWithEmailAsync(companyId, email));
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.False(await db.CandidateDocuments.AnyAsync(d => d.CompanyId == companyId));
    }

    [Fact]
    public async Task Post_Allows_Same_Email_Already_Used_In_Another_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        await RecruitmentTestSeeder.SeedCandidateAsync(_factory, otherCompanyId, Now, email: email);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId), BuildForm(email: email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountCandidatesWithEmailAsync(companyId, email));
        Assert.Equal(1, await CountCandidatesWithEmailAsync(otherCompanyId, email));
    }


    [Fact]
    public async Task Post_Returns_NotFound_For_Unknown_Vacancy_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, Guid.NewGuid()), BuildForm(email: email, cv: PdfCv()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Vacancy_Belonging_To_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var vacancyOfA = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyA, Now);
        var email = UniqueEmail();
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsync(CreateUrl(companyB, vacancyOfA), BuildForm(email: email));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyA, email));
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyB, email));
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyOfA));
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Unknown_ExternalRecruiter()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: email, source: "ExternalRecruiter", recruiterId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
    }


    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_FirstName_Missing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId), BuildForm(firstName: null, email: email));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Email_Invalid()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId), BuildForm(email: "not-an-email"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_ExternalRecruiter_Source_Without_Recruiter_Id()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: email, source: "ExternalRecruiter"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
    }

    [Fact]
    public async Task Post_Returns_BadRequest_For_Disallowed_Cv_File_Type_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var email = UniqueEmail();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsync(CreateUrl(companyId, vacancyId),
            BuildForm(email: email, cv: ("notes.txt", "text/plain", "plain text, not a CV"u8.ToArray())));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.Equal("validation", body!.Code);
        Assert.Equal(0, await CountCandidatesWithEmailAsync(companyId, email));
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.False(await db.CandidateDocumentDeletionOperations.AnyAsync(o => o.CompanyId == companyId));
    }

    private sealed record CreatedPayload(
        Guid CandidateId,
        Guid ApplicationId,
        Guid CompanyId,
        Guid VacancyId,
        string FirstName,
        string LastName,
        string Email,
        Guid CurrentStageId,
        Guid? CvDocumentId,
        string? Source,
        Guid? SourceExternalRecruiterId,
        DateTimeOffset AppliedAt);

    private sealed record DuplicatePayload(
        string Error,
        string Code,
        Guid ExistingCandidateId,
        string ExistingCandidateFirstName,
        string ExistingCandidateLastName,
        string ExistingCandidateEmail,
        bool ExistingCandidateIsActive);

    private sealed record ProblemPayload(string Error, string Code);

    private sealed record DocumentListPayload(IReadOnlyList<DocumentListItemPayload> Items);

    private sealed record DocumentListItemPayload(
        Guid Id, string Title, string Kind, string FileName, long FileSize, string ContentType,
        DateTimeOffset CreatedAt, bool IsCurrentCv, int ReferencingApplicationCount);
}
