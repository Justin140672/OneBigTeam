using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateCandidate;
using HR.Modules.Recruitment.Features.CreateCandidateApplication;
using HR.Modules.Recruitment.Features.UpdateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Tests;

public class CandidateEmailUniquenessPostgresTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const int Rounds = 5;

    private static readonly DateTime FixedUtcNow = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    // ── Real constraint ────────────────────────────────────────────────────

    [Fact]
    public async Task Saving_Case_Variant_Emails_In_Same_Company_Violates_Unique_Index()
    {
        var companyId = Guid.NewGuid();
        var local = $"constraint.{Guid.NewGuid():N}";

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.Add(Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"{local}@example.com", null, null, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.Add(Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"  {local.ToUpperInvariant()}@EXAMPLE.COM ", null, null, Now));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(CandidateEmailUniqueness.IsViolation(ex));
        }

        await using var verify = fixture.BuildContext();
        Assert.Single(await verify.Candidates.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Saving_Same_Email_In_Different_Companies_Succeeds()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var email = $"shared.{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.Add(Candidate.Create(Guid.NewGuid(), companyA, "Emma", "Clarke", email, null, null, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            db.Candidates.Add(Candidate.Create(Guid.NewGuid(), companyB, "Emma", "Clarke", email.ToUpperInvariant(), null, null, Now));
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.BuildContext();
        Assert.Equal(2, await verify.Candidates.AsNoTracking()
            .CountAsync(c => (c.CompanyId == companyA || c.CompanyId == companyB) && c.NormalisedEmail == email));
    }

    [Fact]
    public async Task IsViolation_Is_False_For_A_Different_Unique_Index()
    {
        var companyId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.RecruitmentStages.Add(RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screening", 1, false, RecruitmentStageTerminalOutcome.None, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            db.RecruitmentStages.Add(RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screening", 2, false, RecruitmentStageTerminalOutcome.None, Now));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<Npgsql.PostgresException>(ex.InnerException);
            Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.False(CandidateEmailUniqueness.IsViolation(ex));
        }
    }

    [Fact]
    public void IsViolation_Is_False_For_Non_Postgres_Inner_Exception()
    {
        Assert.False(CandidateEmailUniqueness.IsViolation(new DbUpdateException("boom")));
        Assert.False(CandidateEmailUniqueness.IsViolation(new DbUpdateException("boom", new InvalidOperationException())));
    }


    [Fact]
    public async Task Concurrent_Legacy_Creates_For_Case_Variants_Create_Exactly_One_Candidate()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var companyId = Guid.NewGuid();
            var local = $"legacy.{Guid.NewGuid():N}";
            var lowerEmail = $"{local}@example.com";
            var upperEmail = $"  {local.ToUpperInvariant()}@EXAMPLE.COM ";

            await using var dbA = fixture.BuildContext();
            await using var dbB = fixture.BuildContext();

            var taskA = Task.Run(() => LegacyHandler(dbA).HandleAsync(LegacyRequest(companyId, lowerEmail), CancellationToken.None));
            var taskB = Task.Run(() => LegacyHandler(dbB).HandleAsync(LegacyRequest(companyId, upperEmail), CancellationToken.None));
            var outcomes = await Task.WhenAll(taskA, taskB);

            var winner = Assert.Single(outcomes, o => o.IsSuccess).Value!;
            var loser = Assert.Single(outcomes, o => o.IsFailure);
            Assert.Equal("conflict", loser.Error.Code);
            var duplicate = Assert.IsType<CreateCandidateDuplicateCandidateResponse>(loser.DuplicateCandidate);
            Assert.Equal(CreateCandidateDuplicateCandidateResponse.ErrorCode, duplicate.Code);
            Assert.Equal(winner.Id, duplicate.ExistingCandidateId);

            await using var verify = fixture.BuildContext();
            var candidate = Assert.Single(await verify.Candidates.AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .ToListAsync());
            Assert.Equal(winner.Id, candidate.Id);
            Assert.Equal(lowerEmail, candidate.NormalisedEmail);
        }
    }


    [Fact]
    public async Task Concurrent_Legacy_Create_And_Combined_Intake_For_Case_Variants_Create_Exactly_One_Candidate()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (companyId, vacancyId) = await SeedCompanyWithVacancyAsync();
            var local = $"mixed.{Guid.NewGuid():N}";
            var lowerEmail = $"{local}@example.com";
            var upperEmail = $" {local.ToUpperInvariant()}@Example.Com  ";

            var legacyEmail = round % 2 == 0 ? lowerEmail : upperEmail;
            var intakeEmail = round % 2 == 0 ? upperEmail : lowerEmail;

            await using var legacyDb = fixture.BuildContext();
            await using var intakeDb = fixture.BuildContext();
            var intakeHandler = BuildIntakeHandler(intakeDb, out var storage);

            var legacyTask = Task.Run(() => LegacyHandler(legacyDb).HandleAsync(LegacyRequest(companyId, legacyEmail), CancellationToken.None));
            var intakeTask = Task.Run(() => intakeHandler.HandleAsync(IntakeRequest(companyId, vacancyId, intakeEmail), Guid.NewGuid(), CancellationToken.None));
            await Task.WhenAll(legacyTask, intakeTask);

            var legacy = await legacyTask;
            var intake = await intakeTask;

            Assert.True(legacy.IsSuccess ^ intake.Result.IsSuccess, "Exactly one of the two paths must succeed.");

            await using var verify = fixture.BuildContext();
            var candidate = Assert.Single(await verify.Candidates.AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .ToListAsync());
            Assert.Equal(lowerEmail, candidate.NormalisedEmail);
            var applications = await verify.Applications.AsNoTracking()
                .Where(a => a.CompanyId == companyId)
                .ToListAsync();

            if (legacy.IsSuccess)
            {
                Assert.Equal(candidate.Id, legacy.Value!.Id);
                Assert.Null(legacy.DuplicateCandidate);

                var duplicate = Assert.IsType<CreateCandidateApplicationDuplicateCandidateResponse>(intake.DuplicateCandidate);
                Assert.Equal(CreateCandidateApplicationDuplicateCandidateResponse.ErrorCode, duplicate.Code);
                Assert.Equal(candidate.Id, duplicate.ExistingCandidateId);
                Assert.Equal("conflict", intake.Result.Error.Code);

                Assert.Empty(applications);
            }
            else
            {
                Assert.Equal(candidate.Id, intake.Result.Value!.CandidateId);
                Assert.Null(intake.DuplicateCandidate);

                Assert.Equal("conflict", legacy.Error.Code);
                var duplicate = Assert.IsType<CreateCandidateDuplicateCandidateResponse>(legacy.DuplicateCandidate);
                Assert.Equal(CreateCandidateDuplicateCandidateResponse.ErrorCode, duplicate.Code);
                Assert.Equal(candidate.Id, duplicate.ExistingCandidateId);

                var application = Assert.Single(applications);
                Assert.Equal(candidate.Id, application.CandidateId);
                Assert.Equal(intake.Result.Value.ApplicationId, application.Id);
            }

            Assert.Empty(storage.Uploads);
        }
    }


    [Fact]
    public async Task Concurrent_Updates_Of_Two_Candidates_To_Case_Variants_Of_Same_Email_Leave_Exactly_One_Owner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var companyId = Guid.NewGuid();
            var emma = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var liam = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, Now);
            await using (var seed = fixture.BuildContext())
            {
                seed.Candidates.AddRange(emma, liam);
                await seed.SaveChangesAsync();
            }

            var local = $"claimed.{Guid.NewGuid():N}";
            var lowerEmail = $"{local}@example.com";
            var upperEmail = $"  {local.ToUpperInvariant()}@EXAMPLE.COM ";

            await using var dbA = fixture.BuildContext();
            await using var dbB = fixture.BuildContext();

            var taskA = Task.Run(() => UpdateHandler(dbA).HandleAsync(UpdateRequest(companyId, emma, lowerEmail), CancellationToken.None));
            var taskB = Task.Run(() => UpdateHandler(dbB).HandleAsync(UpdateRequest(companyId, liam, upperEmail), CancellationToken.None));
            var outcomes = await Task.WhenAll(taskA, taskB);

            var winner = Assert.Single(outcomes, o => o.IsSuccess).Value!;
            var loser = Assert.Single(outcomes, o => o.IsFailure);
            Assert.Equal("conflict", loser.Error.Code);

            await using var verify = fixture.BuildContext();
            var owner = Assert.Single(await verify.Candidates.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.NormalisedEmail == lowerEmail)
                .ToListAsync());
            Assert.Equal(winner.Id, owner.Id);

            var loserId = winner.Id == emma.Id ? liam.Id : emma.Id;
            var loserRow = await verify.Candidates.AsNoTracking().SingleAsync(c => c.Id == loserId);
            var originalLoserEmail = loserId == emma.Id ? emma.NormalisedEmail : liam.NormalisedEmail;
            Assert.Equal(originalLoserEmail, loserRow.NormalisedEmail);
            Assert.Equal(1, loserRow.Version);
        }
    }

    [Fact]
    public async Task Update_To_Case_Variant_Of_Existing_Email_Returns_Conflict_On_Postgres()
    {
        var companyId = Guid.NewGuid();
        var emma = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        var local = $"taken.{Guid.NewGuid():N}";
        var other = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"{local}@example.com", null, null, Now);
        await using (var seed = fixture.BuildContext())
        {
            seed.Candidates.AddRange(emma, other);
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.BuildContext();
        var result = await UpdateHandler(db).HandleAsync(
            UpdateRequest(companyId, emma, $" {local.ToUpperInvariant()}@Example.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }


    private static CreateCandidateHandler LegacyHandler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), NullLogger<CreateCandidateHandler>.Instance);

    private static CreateCandidateRequest LegacyRequest(Guid companyId, string email) =>
        new()
        {
            CompanyId = companyId,
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = email,
        };

    private static UpdateCandidateHandler UpdateHandler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static UpdateCandidateRequest UpdateRequest(Guid companyId, Candidate candidate, string email) =>
        new()
        {
            CompanyId       = companyId,
            CandidateId     = candidate.Id,
            FirstName       = candidate.FirstName,
            LastName        = candidate.LastName,
            Email           = email,
            ExpectedVersion = 1,
        };

    private static CreateCandidateApplicationHandler BuildIntakeHandler(RecruitmentDbContext db, out FakeCandidateDocumentStorageService storage)
    {
        storage = new FakeCandidateDocumentStorageService();
        var intake = new CandidateApplicationIntake(
            db,
            storage,
            Options.Create(new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            new FakeAuditPublisher(),
            new RecruitmentStageSeeder(db),
            NullLogger<CandidateApplicationIntake>.Instance);
        return new CreateCandidateApplicationHandler(intake);
    }

    private static CreateCandidateApplicationRequest IntakeRequest(Guid companyId, Guid vacancyId, string email) =>
        new()
        {
            CompanyId = companyId,
            VacancyId = vacancyId,
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = email,
            CvFile    = null,
        };

    private async Task<(Guid CompanyId, Guid VacancyId)> SeedCompanyWithVacancyAsync()
    {
        var companyId = Guid.NewGuid();
        await using var db = fixture.BuildContext();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-1));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now.AddDays(-1));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return (companyId, vacancy.Id);
    }
}
