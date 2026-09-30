using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApplyForInternalVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 4: real-PostgreSQL coverage (via <see cref="RecruitmentDatabaseFixture"/>,
/// which runs every migration — including InternalRecruitment04_AddCandidateEmployeeUniqueness) of the
/// employee self-application guarantees in <see cref="ApplyForInternalVacancyHandler"/>: the
/// (company, employee) advisory lock, the retry when the employee's linked candidate appears
/// concurrently, and the <c>ix_candidates_company_id_employee_id</c> filtered unique index. EF InMemory
/// skips locks and ignores indexes, so these can only be proven against Postgres.
///
/// Deterministic by construction (mirrors <see cref="CreateCandidateApplicationConcurrencyTests"/>):
/// both calls run concurrently via Task.WhenAll with a fresh company per round, and the assertions are
/// on the pair of outcomes and the final database/storage state — never on which call won, or on
/// whether the loser was stopped by the pre-check, the re-check under the lock, or the retry.
/// </summary>
public class ApplyForInternalVacancyConcurrencyTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const int Rounds = 5;

    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private sealed record Participant(
        RecruitmentDbContext Db,
        FakeCandidateDocumentStorageService Storage,
        ApplyForInternalVacancyHandler Handler);

    private Participant BuildParticipant(IEmployeeApplicantReader reader)
    {
        var db = fixture.BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var handler = new ApplyForInternalVacancyHandler(
            db,
            reader,
            storage,
            Options.Create(new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            new FakeAuditPublisher(),
            new RecruitmentStageSeeder(db),
            NullLogger<ApplyForInternalVacancyHandler>.Instance);
        return new Participant(db, storage, handler);
    }

    private sealed record Round(Guid CompanyId, Guid EmployeeId, Guid VacancyA, Guid VacancyB, FakeEmployeeApplicantReader Reader);

    private async Task<Round> SeedRoundAsync()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-10));
            var a = OpenAdvertised(companyId, "Senior Software Engineer");
            var b = OpenAdvertised(companyId, "Engineering Manager");
            db.Vacancies.AddRange(a, b);
            await db.SaveChangesAsync();

            var reader = new FakeEmployeeApplicantReader(FakeEmployeeApplicantReader.Profile(
                companyId, employeeId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example"));

            return new Round(companyId, employeeId, a.Id, b.Id, reader);
        }
    }

    private static Vacancy OpenAdvertised(Guid companyId, string title)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, null, Guid.NewGuid(), Now.AddDays(-10),
            assignedRecruiterId: null, isAdvertisedInternally: true);
        vacancy.Open(Now.AddDays(-10), DateOnly.FromDateTime(Now.AddDays(-10).UtcDateTime));
        return vacancy;
    }

    private static IFormFile FakePdf(string fileName) =>
        new FormFile(new MemoryStream(new byte[2048]), 0, 2048, "CvFile", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static ApplyForInternalVacancyRequest Request(Guid companyId, Guid vacancyId, string fileName) =>
        new() { CompanyId = companyId, VacancyId = vacancyId, CvFile = FakePdf(fileName) };

    private static async Task<(ApplyForInternalVacancyResult First, ApplyForInternalVacancyResult Second)> RaceAsync(
        Participant a, ApplyForInternalVacancyRequest requestA,
        Participant b, ApplyForInternalVacancyRequest requestB,
        Guid employeeId)
    {
        var taskA = Task.Run(() => a.Handler.HandleAsync(requestA, employeeId, CancellationToken.None));
        var taskB = Task.Run(() => b.Handler.HandleAsync(requestB, employeeId, CancellationToken.None));
        var results = await Task.WhenAll(taskA, taskB);
        return (results[0], results[1]);
    }

    [Fact]
    public async Task Concurrent_Applies_By_Same_Employee_To_Same_Vacancy_Yield_One_Application_And_One_Already_Applied()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var r = await SeedRoundAsync();
            var a = BuildParticipant(r.Reader);
            var b = BuildParticipant(r.Reader);
            await using (a.Db)
            await using (b.Db)
            {
                var (first, second) = await RaceAsync(
                    a, Request(r.CompanyId, r.VacancyA, "a-cv.pdf"),
                    b, Request(r.CompanyId, r.VacancyA, "b-cv.pdf"),
                    r.EmployeeId);

                var outcomes = new[] { first, second };
                var winner = Assert.Single(outcomes, o => o.Result.IsSuccess).Result.Value!;
                var loser = Assert.Single(outcomes, o => o.Result.IsFailure);
                var rejection = Assert.IsType<ApplyForInternalVacancyRejection>(loser.Rejection);
                Assert.Equal(ApplyForInternalVacancyRejection.AlreadyAppliedCode, rejection.Code);
                Assert.Equal(StatusCodes.Status409Conflict, rejection.StatusCode);

                await using var verify = fixture.BuildContext();

                var candidate = Assert.Single(await verify.Candidates.AsNoTracking()
                    .Where(c => c.CompanyId == r.CompanyId)
                    .ToListAsync());
                Assert.Equal(r.EmployeeId, candidate.EmployeeId);
                Assert.Equal(winner.CandidateId, candidate.Id);

                var application = Assert.Single(await verify.Applications.AsNoTracking()
                    .Where(x => x.CompanyId == r.CompanyId)
                    .ToListAsync());
                Assert.Equal(winner.ApplicationId, application.Id);
                Assert.Equal(candidate.Id, application.CandidateId);
                Assert.Equal(ApplicationSource.Internal, application.Source);
                Assert.Equal(winner.CvDocumentId, application.CvDocumentId);

                var document = Assert.Single(await verify.CandidateDocuments.AsNoTracking()
                    .Where(d => d.CompanyId == r.CompanyId)
                    .ToListAsync());
                Assert.Equal(winner.CvDocumentId, document.Id);
                Assert.Equal(candidate.Id, document.CandidateId);
                Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
                Assert.Equal(r.EmployeeId, document.UploadedBy);

                var intents = await verify.CandidateDocumentDeletionOperations.AsNoTracking()
                    .Where(o => o.CompanyId == r.CompanyId)
                    .ToListAsync();
                Assert.NotEmpty(intents);
                Assert.All(intents, i => Assert.NotNull(i.ConfirmedAt));
                Assert.Contains(intents, i => i.StorageKey == document.StorageKey);

                var uploads = a.Storage.Uploads.Concat(b.Storage.Uploads).Select(u => u.StorageKey).ToList();
                var deletions = a.Storage.Deletions.Concat(b.Storage.Deletions).ToList();
                Assert.Contains(document.StorageKey, uploads);
                Assert.DoesNotContain(document.StorageKey, deletions);
                Assert.All(uploads.Where(k => k != document.StorageKey), k => Assert.Contains(k, deletions));
                Assert.Equal(uploads.Count - 1, deletions.Count);
                Assert.Equal(uploads.Count, intents.Count);
            }
        }
    }

    [Fact]
    public async Task Concurrent_Applies_By_Same_New_Employee_To_Two_Vacancies_Both_Succeed_On_One_Linked_Candidate()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var r = await SeedRoundAsync();
            var a = BuildParticipant(r.Reader);
            var b = BuildParticipant(r.Reader);
            await using (a.Db)
            await using (b.Db)
            {
                var (first, second) = await RaceAsync(
                    a, Request(r.CompanyId, r.VacancyA, "a-cv.pdf"),
                    b, Request(r.CompanyId, r.VacancyB, "b-cv.pdf"),
                    r.EmployeeId);

                Assert.True(first.Result.IsSuccess, first.Result.IsFailure ? first.Result.Error.Message : null);
                Assert.True(second.Result.IsSuccess, second.Result.IsFailure ? second.Result.Error.Message : null);
                Assert.Equal(first.Result.Value!.CandidateId, second.Result.Value!.CandidateId);

                await using var verify = fixture.BuildContext();

                var candidate = Assert.Single(await verify.Candidates.AsNoTracking()
                    .Where(c => c.CompanyId == r.CompanyId)
                    .ToListAsync());
                Assert.Equal(r.EmployeeId, candidate.EmployeeId);
                Assert.Equal(first.Result.Value.CandidateId, candidate.Id);

                var applications = await verify.Applications.AsNoTracking()
                    .Where(x => x.CompanyId == r.CompanyId)
                    .ToListAsync();
                Assert.Equal(2, applications.Count);
                Assert.All(applications, x => Assert.Equal(candidate.Id, x.CandidateId));
                Assert.All(applications, x => Assert.Equal(ApplicationSource.Internal, x.Source));
                Assert.Equal(
                    new[] { r.VacancyA, r.VacancyB }.OrderBy(x => x),
                    applications.Select(x => x.VacancyId).OrderBy(x => x));

                var documents = await verify.CandidateDocuments.AsNoTracking()
                    .Where(d => d.CompanyId == r.CompanyId)
                    .ToListAsync();
                Assert.Equal(2, documents.Count);
                Assert.All(documents, d => Assert.Equal(candidate.Id, d.CandidateId));
                Assert.Equal(
                    documents.Select(d => d.Id).OrderBy(x => x),
                    applications.Select(x => x.CvDocumentId!.Value).OrderBy(x => x));

                var intents = await verify.CandidateDocumentDeletionOperations.AsNoTracking()
                    .Where(o => o.CompanyId == r.CompanyId)
                    .ToListAsync();
                Assert.All(intents, i => Assert.NotNull(i.ConfirmedAt));

                var keptKeys = documents.Select(d => d.StorageKey).ToHashSet();
                var uploads = a.Storage.Uploads.Concat(b.Storage.Uploads).Select(u => u.StorageKey).ToList();
                var deletions = a.Storage.Deletions.Concat(b.Storage.Deletions).ToList();
                Assert.All(keptKeys, k => Assert.Contains(k, uploads));
                Assert.All(keptKeys, k => Assert.DoesNotContain(k, deletions));
                Assert.All(uploads.Where(k => !keptKeys.Contains(k)), k => Assert.Contains(k, deletions));
                Assert.Equal(uploads.Count - 2, deletions.Count);
                Assert.Equal(uploads.Count, intents.Count);
                Assert.All(keptKeys, k => Assert.Contains(intents, i => i.StorageKey == k));
            }
        }
    }


    [Fact]
    public async Task Unique_Index_Rejects_Two_Candidates_Linked_To_Same_Employee_In_Same_Company()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.Add(Candidate.CreateForEmployee(
                Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example", null, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            var second = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", $"priya.personal.{Guid.NewGuid():N}@example.com", null, Now);
            second.LinkToEmployee(employeeId, Now);
            db.Candidates.Add(second);

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("23505", postgres.SqlState);
            Assert.Equal("ix_candidates_company_id_employee_id", postgres.ConstraintName);
            Assert.Equal(ApplyForInternalVacancyHandler.CandidateEmployeeUniqueIndexName, postgres.ConstraintName);
        }

        await using var verify = fixture.BuildContext();
        Assert.Equal(1, await verify.Candidates.CountAsync(c => c.CompanyId == companyId && c.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Unique_Index_Allows_Many_Unlinked_Candidates_In_Same_Company()
    {
        var companyId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.AddRange(
                Candidate.Create(Guid.NewGuid(), companyId, "Ann", "One", $"ann.{Guid.NewGuid():N}@example.com", null, Now),
                Candidate.Create(Guid.NewGuid(), companyId, "Ben", "Two", $"ben.{Guid.NewGuid():N}@example.com", null, Now),
                Candidate.Create(Guid.NewGuid(), companyId, "Cat", "Three", $"cat.{Guid.NewGuid():N}@example.com", null, Now));
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.BuildContext();
        Assert.Equal(3, await verify.Candidates.CountAsync(c => c.CompanyId == companyId && c.EmployeeId == null));
    }

    [Fact]
    public async Task Unique_Index_Allows_Same_Employee_Id_In_Different_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var email = $"priya.{Guid.NewGuid():N}@acme.example";

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.AddRange(
                Candidate.CreateForEmployee(Guid.NewGuid(), companyA, employeeId, "Priya", "Shah", email, null, Now),
                Candidate.CreateForEmployee(Guid.NewGuid(), companyB, employeeId, "Priya", "Shah", email, null, Now));
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.BuildContext();
        Assert.Equal(2, await verify.Candidates.CountAsync(c => c.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Unique_Index_Allows_Different_Employees_In_Same_Company()
    {
        var companyId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.AddRange(
                Candidate.CreateForEmployee(Guid.NewGuid(), companyId, Guid.NewGuid(), "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example", null, Now),
                Candidate.CreateForEmployee(Guid.NewGuid(), companyId, Guid.NewGuid(), "Tom", "Baker", $"tom.{Guid.NewGuid():N}@acme.example", null, Now));
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.BuildContext();
        Assert.Equal(2, await verify.Candidates.CountAsync(c => c.CompanyId == companyId && c.EmployeeId != null));
    }
}
