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
/// Internal recruitment Ticket 3: real-PostgreSQL coverage (via <see cref="RecruitmentDatabaseFixture"/>)
/// of the duplicate-email guarantee in <see cref="CandidateApplicationIntake"/>. There is no unique
/// constraint on (company_id, email); two concurrent intakes are serialised by a transaction-scoped
/// advisory lock on (company, lower(email)) and the duplicate is re-checked under that lock. EF
/// InMemory skips the lock entirely, so this can only be proven against Postgres.
///
/// Deterministic by construction: both calls run concurrently via Task.WhenAll, and the assertions
/// are on the final database/storage state and on the pair of outcomes — never on which call won, or
/// on whether the loser was stopped by the cheap pre-check or by the re-check under the lock.
/// </summary>
public class CreateCandidateApplicationConcurrencyTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const int Rounds = 5;

    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private sealed record Participant(
        RecruitmentDbContext Db,
        FakeCandidateDocumentStorageService Storage,
        CreateCandidateApplicationHandler Handler);

    private Participant BuildParticipant()
    {
        var db = fixture.BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var intake = new CandidateApplicationIntake(
            db,
            storage,
            Options.Create(new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            new FakeAuditPublisher(),
            new RecruitmentStageSeeder(db),
            NullLogger<CandidateApplicationIntake>.Instance);
        return new Participant(db, storage, new CreateCandidateApplicationHandler(intake));
    }

    private async Task<(Guid CompanyId, Guid VacancyId)> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        await using var db = fixture.BuildContext();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-1));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now.AddDays(-1));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return (companyId, vacancy.Id);
    }

    private static IFormFile FakePdf(string fileName) =>
        new FormFile(new MemoryStream(PdfBytes.Create(2048)), 0, 2048, "CvFile", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static CreateCandidateApplicationRequest Request(Guid companyId, Guid vacancyId, string email, IFormFile? cv) =>
        new()
        {
            CompanyId = companyId,
            VacancyId = vacancyId,
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = email,
            CvFile    = cv,
        };

    private static async Task<(CreateCandidateApplicationResult First, CreateCandidateApplicationResult Second)> RaceAsync(
        Participant a, CreateCandidateApplicationRequest requestA,
        Participant b, CreateCandidateApplicationRequest requestB)
    {
        var performedBy = Guid.NewGuid();
        var taskA = Task.Run(() => a.Handler.HandleAsync(requestA, performedBy, CancellationToken.None));
        var taskB = Task.Run(() => b.Handler.HandleAsync(requestB, performedBy, CancellationToken.None));
        var results = await Task.WhenAll(taskA, taskB);
        return (results[0], results[1]);
    }

    private static (CreateCandidateApplicationResponse Winner, CreateCandidateApplicationDuplicateCandidateResponse Loser) AssertOneWinnerOneDuplicate(
        CreateCandidateApplicationResult first, CreateCandidateApplicationResult second)
    {
        var outcomes = new[] { first, second };
        var successes = outcomes.Where(o => o.Result.IsSuccess).ToList();
        var duplicates = outcomes.Where(o => o.DuplicateCandidate is not null).ToList();

        var winner = Assert.Single(successes).Result.Value!;
        var loser = Assert.Single(duplicates).DuplicateCandidate!;

        Assert.Equal(CreateCandidateApplicationDuplicateCandidateResponse.ErrorCode, loser.Code);
        Assert.Equal(winner.CandidateId, loser.ExistingCandidateId);
        Assert.True(loser.ExistingCandidateIsActive);
        return (winner, loser);
    }

    [Fact]
    public async Task Concurrent_Intakes_For_Same_Email_Differing_Only_In_Case_Create_Exactly_One_Candidate_And_Application()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (companyId, vacancyId) = await SeedCompanyAsync();
            var local = $"race.{Guid.NewGuid():N}";
            var lowerEmail = $"{local}@example.com";
            var upperEmail = $"  {local.ToUpperInvariant()}@EXAMPLE.COM ";

            var a = BuildParticipant();
            var b = BuildParticipant();
            await using (a.Db)
            await using (b.Db)
            {
                var (first, second) = await RaceAsync(
                    a, Request(companyId, vacancyId, lowerEmail, cv: null),
                    b, Request(companyId, vacancyId, upperEmail, cv: null));

                var (winner, _) = AssertOneWinnerOneDuplicate(first, second);

                await using var verify = fixture.BuildContext();
                var candidates = await verify.Candidates.AsNoTracking()
                    .Where(c => c.CompanyId == companyId)
                    .ToListAsync();
                var candidate = Assert.Single(candidates);
                Assert.Equal(winner.CandidateId, candidate.Id);
                Assert.Equal(lowerEmail, candidate.Email.ToLowerInvariant());

                var application = Assert.Single(await verify.Applications.AsNoTracking()
                    .Where(x => x.CompanyId == companyId)
                    .ToListAsync());
                Assert.Equal(winner.ApplicationId, application.Id);
                Assert.Equal(candidate.Id, application.CandidateId);
                Assert.Equal(vacancyId, application.VacancyId);

                Assert.Empty(a.Storage.Uploads);
                Assert.Empty(b.Storage.Uploads);
            }
        }
    }

    [Fact]
    public async Task Concurrent_Intakes_With_Cv_Keep_Only_The_Winners_Blob_And_Leave_No_Unconfirmed_Intent()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (companyId, vacancyId) = await SeedCompanyAsync();
            var local = $"race.cv.{Guid.NewGuid():N}";
            var lowerEmail = $"{local}@example.com";
            var mixedEmail = $"{local.ToUpperInvariant()}@Example.com";

            var a = BuildParticipant();
            var b = BuildParticipant();
            await using (a.Db)
            await using (b.Db)
            {
                var (first, second) = await RaceAsync(
                    a, Request(companyId, vacancyId, lowerEmail, FakePdf("a-cv.pdf")),
                    b, Request(companyId, vacancyId, mixedEmail, FakePdf("b-cv.pdf")));

                var (winner, _) = AssertOneWinnerOneDuplicate(first, second);
                Assert.NotNull(winner.CvDocumentId);

                await using var verify = fixture.BuildContext();

                Assert.Single(await verify.Candidates.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync());
                var application = Assert.Single(await verify.Applications.AsNoTracking()
                    .Where(x => x.CompanyId == companyId)
                    .ToListAsync());
                Assert.Equal(winner.CvDocumentId, application.CvDocumentId);

                var document = Assert.Single(await verify.CandidateDocuments.AsNoTracking()
                    .Where(d => d.CompanyId == companyId)
                    .ToListAsync());
                Assert.Equal(winner.CvDocumentId, document.Id);
                Assert.Equal(winner.CandidateId, document.CandidateId);
                Assert.Equal(CandidateDocumentKind.Cv, document.Kind);

                var intents = await verify.CandidateDocumentDeletionOperations.AsNoTracking()
                    .Where(o => o.CompanyId == companyId)
                    .ToListAsync();
                Assert.NotEmpty(intents);
                Assert.All(intents, i => Assert.NotNull(i.ConfirmedAt));
                Assert.Contains(intents, i => i.StorageKey == document.StorageKey);

                var uploads = a.Storage.Uploads.Concat(b.Storage.Uploads).Select(u => u.StorageKey).ToList();
                var deletions = a.Storage.Deletions.Concat(b.Storage.Deletions).ToList();
                Assert.Contains(document.StorageKey, uploads);
                Assert.DoesNotContain(document.StorageKey, deletions);
                Assert.InRange(uploads.Count, 1, 2);
                Assert.All(uploads.Where(k => k != document.StorageKey), k => Assert.Contains(k, deletions));
                Assert.Equal(uploads.Count - 1, deletions.Count);
                Assert.Equal(uploads.Count, intents.Count);
            }
        }
    }
}
