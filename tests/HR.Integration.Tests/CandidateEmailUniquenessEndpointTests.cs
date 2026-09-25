using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// [P1] Case-insensitive candidate email uniqueness, end to end against PostgreSQL. Every candidate
/// write path — legacy POST /candidates, the combined multipart POST .../applications/new-candidate,
/// and PUT /candidates/{id} — treats emails that differ only in case or surrounding whitespace as the
/// same candidate within a company, and a lost race is always a 409 (never a 500).
///
/// Status codes: FluentValidation failures are 422 (HR.Api configures FastEndpoints with
/// <c>Errors.StatusCode = 422</c>); duplicates on either create path are 409 with the
/// <c>candidate_email_exists</c> body; PUT duplicates are 409 <c>{ error }</c>.
/// See CandidateEmailUniquenessPostgresTests / CandidateEmailMigrationTests in
/// HR.Modules.Recruitment.Tests for handler-level race and migration coverage.
/// </summary>
[Collection("Integration")]
public class CandidateEmailUniquenessEndpointTests
{
    private const int Rounds = 3;

    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00e1a0-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CandidateEmailUniquenessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> RecruiterClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    private static string LegacyUrl(Guid companyId) => $"/api/companies/{companyId}/candidates";

    private static string IntakeUrl(Guid companyId, Guid vacancyId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate";

    private static string UniqueLocalPart(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    /// <summary>Returns (lower, upper-padded) variants of a unique email.</summary>
    private static (string Lower, string Variant) EmailVariants(string prefix)
    {
        var local = UniqueLocalPart(prefix);
        return ($"{local}@example.com", $"  {local.ToUpperInvariant()}@Example.COM ");
    }

    private static Task<HttpResponseMessage> PostLegacyAsync(HttpClient client, Guid companyId, string email,
        string firstName = "Emma", string lastName = "Clarke") =>
        client.PostAsJsonAsync(LegacyUrl(companyId), new { companyId, firstName, lastName, email });

    private static Task<HttpResponseMessage> PostIntakeAsync(HttpClient client, Guid companyId, Guid vacancyId, string email)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent("Emma"), "FirstName" },
            { new StringContent("Clarke"), "LastName" },
            { new StringContent(email), "Email" },
        };
        return client.PostAsync(IntakeUrl(companyId, vacancyId), content);
    }

    private async Task<List<(Guid Id, string NormalisedEmail)>> CandidatesForCompanyAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var rows = await db.Candidates.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.NormalisedEmail })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.NormalisedEmail)).ToList();
    }

    private async Task<int> CountApplicationsForVacancyAsync(Guid vacancyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.CountAsync(a => a.VacancyId == vacancyId);
    }

    private static async Task<DuplicatePayload> AssertDuplicateAsync(HttpResponseMessage response, Guid expectedExistingId)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DuplicatePayload>();
        Assert.NotNull(body);
        Assert.Equal("candidate_email_exists", body!.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Error));
        Assert.Equal(expectedExistingId, body.ExistingCandidateId);
        return body;
    }

    // ---- Legacy create -----------------------------------------------------------------------------

    [Fact]
    public async Task Post_Legacy_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await PostLegacyAsync(client, companyId, $"{UniqueLocalPart("anon")}@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Legacy_Creates_Candidate_And_Persists_NormalisedEmail()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var (lower, variant) = EmailVariants("happy");

        var response = await PostLegacyAsync(client, companyId, variant);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.NotNull(created);
        Assert.Equal(variant.Trim(), created!.Email);

        var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(lower, row.NormalisedEmail);
    }

    [Fact]
    public async Task Post_Legacy_Returns_Conflict_For_Case_And_Whitespace_Variant_In_Same_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var (lower, variant) = EmailVariants("dup");

        var first = await PostLegacyAsync(client, companyId, lower, "Liam", "Turner");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<CandidatePayload>())!;

        var second = await PostLegacyAsync(client, companyId, variant);

        var body = await AssertDuplicateAsync(second, created.Id);
        Assert.Equal("Liam", body.ExistingCandidateFirstName);
        Assert.Equal("Turner", body.ExistingCandidateLastName);
        Assert.Equal(lower, body.ExistingCandidateEmail);
        Assert.True(body.ExistingCandidateIsActive);

        var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(lower, row.NormalisedEmail);
    }

    [Fact]
    public async Task Post_Legacy_Allows_Same_Email_In_Different_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (lower, variant) = EmailVariants("shared");
        using var clientA = await RecruiterClientAsync(companyA);
        using var clientB = await RecruiterClientAsync(companyB);

        var responseA = await PostLegacyAsync(clientA, companyA, lower);
        var responseB = await PostLegacyAsync(clientB, companyB, variant);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        Assert.Single(await CandidatesForCompanyAsync(companyA));
        Assert.Single(await CandidatesForCompanyAsync(companyB));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Legacy_Returns_UnprocessableEntity_For_Invalid_Email(string email)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);

        var response = await PostLegacyAsync(client, companyId, email);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await CandidatesForCompanyAsync(companyId));
    }

    // ---- Legacy create vs combined intake ----------------------------------------------------------

    [Fact]
    public async Task Intake_After_Legacy_Create_With_Case_Variant_Returns_Conflict_And_Creates_No_Application()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await RecruiterClientAsync(companyId);
        var (lower, variant) = EmailVariants("legacyfirst");

        var legacy = await PostLegacyAsync(client, companyId, lower);
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        var legacyCandidate = (await legacy.Content.ReadFromJsonAsync<CandidatePayload>())!;

        var intake = await PostIntakeAsync(client, companyId, vacancyId, variant);

        await AssertDuplicateAsync(intake, legacyCandidate.Id);
        Assert.Single(await CandidatesForCompanyAsync(companyId));
        Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));
    }

    [Fact]
    public async Task Legacy_Create_After_Intake_With_Case_Variant_Returns_Conflict_Identifying_Intake_Candidate()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await RecruiterClientAsync(companyId);
        var (lower, variant) = EmailVariants("intakefirst");

        var intake = await PostIntakeAsync(client, companyId, vacancyId, variant);
        Assert.Equal(HttpStatusCode.Created, intake.StatusCode);
        var intakeCreated = (await intake.Content.ReadFromJsonAsync<IntakePayload>())!;

        var legacy = await PostLegacyAsync(client, companyId, lower);

        await AssertDuplicateAsync(legacy, intakeCreated.CandidateId);
        var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
        Assert.Equal(intakeCreated.CandidateId, row.Id);
        Assert.Equal(lower, row.NormalisedEmail);
        Assert.Equal(1, await CountApplicationsForVacancyAsync(vacancyId));
    }

    // ---- Concurrency --------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_Legacy_Posts_For_Case_Variants_Return_One_Created_And_One_Conflict()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var companyId = Guid.NewGuid();
            using var client = await RecruiterClientAsync(companyId);
            var (lower, variant) = EmailVariants($"race{round}");

            var responses = await Task.WhenAll(
                Task.Run(() => PostLegacyAsync(client, companyId, lower)),
                Task.Run(() => PostLegacyAsync(client, companyId, variant)));

            Assert.Equal(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray());

            var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.Created)
                .Content.ReadFromJsonAsync<CandidatePayload>())!;
            await AssertDuplicateAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), winner.Id);

            var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
            Assert.Equal(winner.Id, row.Id);
            Assert.Equal(lower, row.NormalisedEmail);
        }
    }

    [Fact]
    public async Task Concurrent_Legacy_Post_And_Intake_Post_For_Case_Variants_Return_One_Created_And_One_Conflict()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var companyId = Guid.NewGuid();
            var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
            using var client = await RecruiterClientAsync(companyId);
            var (lower, variant) = EmailVariants($"mixedrace{round}");

            var legacyTask = Task.Run(() => PostLegacyAsync(client, companyId, round % 2 == 0 ? lower : variant));
            var intakeTask = Task.Run(() => PostIntakeAsync(client, companyId, vacancyId, round % 2 == 0 ? variant : lower));
            await Task.WhenAll(legacyTask, intakeTask);
            var legacy = await legacyTask;
            var intake = await intakeTask;

            Assert.Equal(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                new[] { legacy.StatusCode, intake.StatusCode }.OrderBy(s => (int)s).ToArray());

            var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
            Assert.Equal(lower, row.NormalisedEmail);

            if (legacy.StatusCode == HttpStatusCode.Created)
            {
                var winner = (await legacy.Content.ReadFromJsonAsync<CandidatePayload>())!;
                Assert.Equal(row.Id, winner.Id);
                await AssertDuplicateAsync(intake, winner.Id);
                Assert.Equal(0, await CountApplicationsForVacancyAsync(vacancyId));
            }
            else
            {
                var winner = (await intake.Content.ReadFromJsonAsync<IntakePayload>())!;
                Assert.Equal(row.Id, winner.CandidateId);
                await AssertDuplicateAsync(legacy, winner.CandidateId);
                Assert.Equal(1, await CountApplicationsForVacancyAsync(vacancyId));
            }
        }
    }

    // ---- Update ------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_Candidate_Returns_Conflict_When_Email_Is_Case_Variant_Of_Another_Candidate()
    {
        var companyId = Guid.NewGuid();
        var (lower, variant) = EmailVariants("taken");
        await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now, "Liam", "Turner", lower);
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await RecruiterClientAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            new { companyId, candidateId, firstName = "Emma", lastName = "Clarke", email = variant, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Error));

        var rows = await CandidatesForCompanyAsync(companyId);
        Assert.Single(rows, r => r.NormalisedEmail == lower);
        Assert.NotEqual(lower, rows.Single(r => r.Id == candidateId).NormalisedEmail);
    }

    [Fact]
    public async Task Put_Candidate_Allows_Case_Only_Change_Of_Own_Email()
    {
        var companyId = Guid.NewGuid();
        var (lower, variant) = EmailVariants("own");
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now, "Emma", "Clarke", lower);
        using var client = await RecruiterClientAsync(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            new { companyId, candidateId, firstName = "Emma", lastName = "Clarke", email = variant, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.Equal(variant.Trim(), payload!.Email);

        var row = Assert.Single(await CandidatesForCompanyAsync(companyId));
        Assert.Equal(lower, row.NormalisedEmail);
    }

    private sealed record CandidatePayload(Guid Id, Guid CompanyId, string FirstName, string LastName, string Email);

    private sealed record IntakePayload(Guid CandidateId, Guid ApplicationId, Guid CompanyId, Guid VacancyId, string Email);

    private sealed record ErrorPayload(string Error);

    private sealed record DuplicatePayload(
        string Error,
        string Code,
        Guid ExistingCandidateId,
        string ExistingCandidateFirstName,
        string ExistingCandidateLastName,
        string ExistingCandidateEmail,
        bool ExistingCandidateIsActive);
}
