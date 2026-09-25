using System.Data.Common;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DeleteCandidateDocument;
using HR.Modules.Recruitment.Features.PurgeEligibleCandidates;
using HR.Modules.Recruitment.Features.SetApplicationCv;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 1: real-PostgreSQL coverage (migrations applied via
/// <see cref="RecruitmentDatabaseFixture"/>) of the guarantees the database itself provides for
/// applications.cv_document_id — the composite FK fk_applications_candidate_documents_cv_document
/// (cv_document_id, candidate_id, company_id) -> candidate_documents (id, candidate_id, company_id),
/// ON DELETE RESTRICT — plus optimistic-concurrency and purge behaviour that EF InMemory cannot model
/// (it enforces no foreign keys). Domain/handler guards are deliberately bypassed (EF property writes
/// or raw SQL) where the point is to prove the database rejects the state on its own.
/// </summary>
public class ApplicationCvDocumentConstraintTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const string CvForeignKeyName = "fk_applications_candidate_documents_cv_document";

    private static readonly DateTime FixedUtcNow = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private sealed record Seed(
        Guid CompanyId,
        Guid VacancyId,
        Guid CandidateId,
        Guid ApplicationId,
        Guid StageId,
        CandidateDocument Cv1,
        CandidateDocument Cv2);

    private async Task<Seed> SeedAsync(bool attachCv1 = false, DateTimeOffset? at = null, bool withdrawn = false)
    {
        var seededAt = at ?? Now.AddDays(-1);
        var companyId = Guid.NewGuid();
        await using var db = fixture.BuildContext();

        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, seededAt);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), seededAt);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, seededAt);
        var cv1 = Document(companyId, candidate.Id, "cv-v1.pdf", seededAt);
        var cv2 = Document(companyId, candidate.Id, "cv-v2.pdf", seededAt.AddMinutes(5));
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, seededAt);
        if (attachCv1)
            application.AttachCv(cv1, seededAt);
        if (withdrawn)
            application.Withdraw(seededAt);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(cv1, cv2);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return new Seed(companyId, vacancy.Id, candidate.Id, application.Id, stages.CvReview.Id, cv1, cv2);
    }

    private static CandidateDocument Document(
        Guid companyId, Guid candidateId, string fileName, DateTimeOffset createdAt,
        CandidateDocumentKind kind = CandidateDocumentKind.Cv) =>
        CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, fileName, fileName, 2048, "application/pdf",
            $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}", Guid.NewGuid(), createdAt, kind);

    private async Task<Guid> SeedCandidateAsync(Guid companyId)
    {
        await using var db = fixture.BuildContext();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private async Task<CandidateDocument> AddDocumentAsync(CandidateDocument document)
    {
        await using var db = fixture.BuildContext();
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private async Task<Application> ReloadApplicationAsync(Guid applicationId)
    {
        await using var db = fixture.BuildContext();
        return await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
    }

    private async Task<bool> DocumentExistsAsync(Guid documentId)
    {
        await using var db = fixture.BuildContext();
        return await db.CandidateDocuments.AnyAsync(d => d.Id == documentId);
    }

    private static PostgresException AssertCvForeignKeyViolation(Exception? ex)
    {
        Assert.NotNull(ex);
        var pg = ex as PostgresException ?? ex!.InnerException as PostgresException;
        Assert.True(pg is not null, $"Expected a PostgresException but got {ex!.GetType().Name}: {ex.Message}");
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg!.SqlState);
        Assert.Equal(CvForeignKeyName, pg.ConstraintName);
        return pg;
    }

    // ---- (a) other candidate's document ----------------------------------------------------------

    [Fact]
    public async Task Inserting_Application_Referencing_Another_Candidates_Cv_Is_Rejected_By_The_Database()
    {
        var seed = await SeedAsync();
        var otherCandidateId = await SeedCandidateAsync(seed.CompanyId);
        var otherCandidatesCv = await AddDocumentAsync(Document(seed.CompanyId, otherCandidateId, "other.pdf", Now));

        // A fresh application for the seeded candidate on a second vacancy, with the FK property
        // written directly (bypassing Application.AttachCv's guard).
        Guid newApplicationId;
        Exception? ex;
        await using (var db = fixture.BuildContext())
        {
            var vacancy = Vacancy.Create(Guid.NewGuid(), seed.CompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var application = Application.Create(Guid.NewGuid(), seed.CompanyId, vacancy.Id, seed.CandidateId, seed.StageId, null, Now);
            newApplicationId = application.Id;
            db.Vacancies.Add(vacancy);
            db.Applications.Add(application);
            db.Entry(application).Property(a => a.CvDocumentId).CurrentValue = otherCandidatesCv.Id;

            ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        }

        Assert.IsAssignableFrom<DbUpdateException>(ex);
        AssertCvForeignKeyViolation(ex);

        await using var verify = fixture.BuildContext();
        Assert.False(await verify.Applications.AnyAsync(a => a.Id == newApplicationId));
    }

    [Fact]
    public async Task Updating_Application_To_Reference_Another_Candidates_Cv_Is_Rejected_By_The_Database()
    {
        var seed = await SeedAsync();
        var otherCandidateId = await SeedCandidateAsync(seed.CompanyId);
        var otherCandidatesCv = await AddDocumentAsync(Document(seed.CompanyId, otherCandidateId, "other.pdf", Now));

        await using var db = fixture.BuildContext();
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recruitment.applications SET cv_document_id = {otherCandidatesCv.Id} WHERE id = {seed.ApplicationId}"));

        AssertCvForeignKeyViolation(ex);
        Assert.Null((await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
    }

    // ---- (b) other company's document -----------------------------------------------------------

    [Fact]
    public async Task Updating_Application_To_Reference_Another_Companys_Document_Is_Rejected_By_The_Database()
    {
        var seed = await SeedAsync();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyCandidateId = await SeedCandidateAsync(otherCompanyId);
        var foreignCv = await AddDocumentAsync(Document(otherCompanyId, otherCompanyCandidateId, "foreign.pdf", Now));

        await using var db = fixture.BuildContext();
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recruitment.applications SET cv_document_id = {foreignCv.Id} WHERE id = {seed.ApplicationId}"));

        AssertCvForeignKeyViolation(ex);
        Assert.Null((await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Company_Is_Part_Of_The_Key_Even_When_The_Candidate_Id_Matches()
    {
        // Isolates the company_id column of the composite FK: the document carries the SAME
        // candidate_id as the application but a different company_id. Only the company differs, so
        // a rejection proves company_id genuinely participates in the constraint.
        var seed = await SeedAsync();
        var crossCompanyDoc = await AddDocumentAsync(Document(Guid.NewGuid(), seed.CandidateId, "cross-company.pdf", Now));

        await using var db = fixture.BuildContext();
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recruitment.applications SET cv_document_id = {crossCompanyDoc.Id} WHERE id = {seed.ApplicationId}"));

        AssertCvForeignKeyViolation(ex);
    }

    [Fact]
    public async Task Referencing_A_Nonexistent_Document_Is_Rejected_By_The_Database()
    {
        var seed = await SeedAsync();

        await using var db = fixture.BuildContext();
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recruitment.applications SET cv_document_id = {Guid.NewGuid()} WHERE id = {seed.ApplicationId}"));

        AssertCvForeignKeyViolation(ex);
    }

    [Fact]
    public async Task Referencing_Own_Candidates_Document_In_Same_Company_Is_Accepted_By_The_Database()
    {
        var seed = await SeedAsync();

        await using (var db = fixture.BuildContext())
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE recruitment.applications SET cv_document_id = {seed.Cv2.Id} WHERE id = {seed.ApplicationId}");
            Assert.Equal(1, affected);
        }

        Assert.Equal(seed.Cv2.Id, (await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
    }

    // ---- (c) delete restricted while referenced --------------------------------------------------

    [Fact]
    public async Task Deleting_A_Referenced_Cv_Row_Fails_With_Foreign_Key_Violation()
    {
        var seed = await SeedAsync(attachCv1: true);

        await using (var db = fixture.BuildContext())
        {
            var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM recruitment.candidate_documents WHERE id = {seed.Cv1.Id}"));

            AssertCvForeignKeyViolation(ex);
        }

        Assert.True(await DocumentExistsAsync(seed.Cv1.Id));
        Assert.Equal(seed.Cv1.Id, (await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
    }

    [Fact]
    public async Task Deleting_A_Referenced_Cv_Through_EF_Throws_DbUpdateException_With_Foreign_Key_Violation()
    {
        var seed = await SeedAsync(attachCv1: true);

        await using (var db = fixture.BuildContext())
        {
            var cv = await db.CandidateDocuments.SingleAsync(d => d.Id == seed.Cv1.Id);
            db.CandidateDocuments.Remove(cv);

            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());

            Assert.IsAssignableFrom<DbUpdateException>(ex);
            AssertCvForeignKeyViolation(ex);
        }

        Assert.True(await DocumentExistsAsync(seed.Cv1.Id));
    }

    [Fact]
    public async Task Deleting_An_Unreferenced_Cv_Of_The_Same_Candidate_Succeeds()
    {
        var seed = await SeedAsync(attachCv1: true);

        await using (var db = fixture.BuildContext())
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM recruitment.candidate_documents WHERE id = {seed.Cv2.Id}");
            Assert.Equal(1, affected);
        }

        Assert.False(await DocumentExistsAsync(seed.Cv2.Id));
    }

    // ---- (d) null reference allowed ---------------------------------------------------------------

    [Fact]
    public async Task Application_With_Null_Cv_Document_Id_Is_Accepted_And_Loads()
    {
        var seed = await SeedAsync();

        var saved = await ReloadApplicationAsync(seed.ApplicationId);

        Assert.Null(saved.CvDocumentId);
        Assert.Equal(seed.CandidateId, saved.CandidateId);
    }

    [Fact]
    public async Task Clearing_The_Reference_With_Raw_Sql_Is_Accepted()
    {
        var seed = await SeedAsync(attachCv1: true);

        await using (var db = fixture.BuildContext())
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE recruitment.applications SET cv_document_id = NULL WHERE id = {seed.ApplicationId}");
            Assert.Equal(1, affected);
        }

        Assert.Null((await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
    }

    // ---- (e) clear then delete --------------------------------------------------------------------

    [Fact]
    public async Task After_Removing_The_Reference_Via_SetApplicationCv_The_Cv_Can_Be_Deleted()
    {
        var seed = await SeedAsync(attachCv1: true);

        await using (var db = fixture.BuildContext())
        {
            var removed = await new SetApplicationCvHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher()).HandleAsync(
                new SetApplicationCvRequest
                {
                    CompanyId = seed.CompanyId,
                    VacancyId = seed.VacancyId,
                    ApplicationId = seed.ApplicationId,
                    CvDocumentId = null,
                    ExpectedVersion = 1,
                },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(removed.IsSuccess);
        }

        await using (var db = fixture.BuildContext())
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM recruitment.candidate_documents WHERE id = {seed.Cv1.Id}");
            Assert.Equal(1, affected);
        }

        Assert.False(await DocumentExistsAsync(seed.Cv1.Id));
    }

    [Fact]
    public async Task DeleteCandidateDocumentHandler_Returns_Conflict_For_Referenced_Cv_And_Commits_Nothing()
    {
        var seed = await SeedAsync(attachCv1: true);
        var storage = new FakeCandidateDocumentStorageService();

        await using (var db = fixture.BuildContext())
        {
            var result = await DeleteHandler(db, storage).HandleAsync(
                new DeleteCandidateDocumentRequest { CompanyId = seed.CompanyId, CandidateId = seed.CandidateId, DocumentId = seed.Cv1.Id },
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("conflict", result.Error.Code);
        }

        Assert.True(await DocumentExistsAsync(seed.Cv1.Id));
        Assert.Empty(storage.Deletions);
        await using var verify = fixture.BuildContext();
        Assert.False(await verify.CandidateDocumentDeletionOperations.AnyAsync(o => o.CandidateId == seed.CandidateId));
    }

    [Fact]
    public async Task DeleteCandidateDocumentHandler_Translates_A_Racing_Reference_Into_Conflict_And_Commits_Nothing()
    {
        // The handler's pre-check sees no referencing application; then, just before its DELETE is
        // sent, another connection commits a reference to the same CV. The FK must reject the delete
        // and the handler must translate that (SqlState 23503) into a clean conflict — with the
        // deletion-operation row (same transaction) rolled back and the blob untouched.
        var seed = await SeedAsync();
        var storage = new FakeCandidateDocumentStorageService();
        var interceptor = new AttachReferenceBeforeDeleteInterceptor(fixture.ConnectionString, seed.ApplicationId, seed.Cv1.Id);

        await using (var db = new RecruitmentDbContext(
            new DbContextOptionsBuilder<RecruitmentDbContext>()
                .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "recruitment"))
                .AddInterceptors(interceptor)
                .Options))
        {
            var result = await DeleteHandler(db, storage).HandleAsync(
                new DeleteCandidateDocumentRequest { CompanyId = seed.CompanyId, CandidateId = seed.CandidateId, DocumentId = seed.Cv1.Id },
                CancellationToken.None);

            Assert.True(interceptor.Fired, "The racing reference was never injected — the DELETE command was not intercepted.");
            Assert.True(result.IsFailure);
            Assert.Equal("conflict", result.Error.Code);
        }

        Assert.True(await DocumentExistsAsync(seed.Cv1.Id));
        Assert.Equal(seed.Cv1.Id, (await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
        Assert.Empty(storage.Deletions);
        await using var verify = fixture.BuildContext();
        Assert.False(await verify.CandidateDocumentDeletionOperations.AnyAsync(o => o.CandidateId == seed.CandidateId));
    }

    // ---- (f) concurrency --------------------------------------------------------------------------

    [Fact]
    public async Task Two_Contexts_Setting_Different_Cvs_From_The_Same_Version_Exactly_One_Wins()
    {
        // Deterministic stale-writer race: context B has already loaded (and tracks) the application
        // at Version 1 before A's change commits, so B's handler passes the up-front version check and
        // reaches Postgres with a genuinely stale "WHERE version = 1" UPDATE that affects zero rows.
        var seed = await SeedAsync();

        await using var ctxB = fixture.BuildContext();
        await ctxB.Applications.SingleAsync(a => a.Id == seed.ApplicationId);

        var auditA = new FakeAuditPublisher();
        await using (var ctxA = fixture.BuildContext())
        {
            var winner = await SetCvHandler(ctxA, auditA).HandleAsync(SetCvRequest(seed, seed.Cv1.Id), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var auditB = new FakeAuditPublisher();
        var loser = await SetCvHandler(ctxB, auditB).HandleAsync(SetCvRequest(seed, seed.Cv2.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);
        Assert.Single(auditA.Published);
        Assert.Empty(auditB.Published);

        var saved = await ReloadApplicationAsync(seed.ApplicationId);
        Assert.Equal(seed.Cv1.Id, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Parallel_SetApplicationCv_Calls_With_The_Same_ExpectedVersion_Exactly_One_Succeeds()
    {
        var seed = await SeedAsync();

        await using var ctxA = fixture.BuildContext();
        await using var ctxB = fixture.BuildContext();
        var auditA = new FakeAuditPublisher();
        var auditB = new FakeAuditPublisher();

        var results = await Task.WhenAll(
            SetCvHandler(ctxA, auditA).HandleAsync(SetCvRequest(seed, seed.Cv1.Id), Guid.NewGuid(), CancellationToken.None),
            SetCvHandler(ctxB, auditB).HandleAsync(SetCvRequest(seed, seed.Cv2.Id), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var failure = Assert.Single(results, r => r.IsFailure);
        Assert.Equal("concurrency", failure.Error.Code);
        Assert.Equal(1, auditA.Published.Count + auditB.Published.Count);

        var winningCvId = results.Single(r => r.IsSuccess).Value!.CvDocumentId;
        var saved = await ReloadApplicationAsync(seed.ApplicationId);
        Assert.Equal(winningCvId, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
    }

    // ---- Purge -------------------------------------------------------------------------------------

    [Fact]
    public async Task Purge_Of_Candidate_Whose_Application_References_A_Cv_Clears_Reference_And_Deletes_The_Cv()
    {
        // With ON DELETE RESTRICT, the purge can only delete the candidate's CV documents if the same
        // save also clears the application's reference (Application.RedactPersonalData).
        var oldEnough = Now.AddDays(-800);
        var seed = await SeedAsync(attachCv1: true, at: oldEnough, withdrawn: true);

        await using (var db = fixture.BuildContext())
        {
            var result = await new PurgeEligibleCandidatesHandler(
                    db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyRecruitmentSettingsReader(),
                    new FakeLegalHoldStatusReader(), new RecordingBackgroundJobClient())
                .HandleAsync(new PurgeEligibleCandidatesRequest { CompanyId = seed.CompanyId }, Guid.NewGuid(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value!.PurgedCount);
        }

        Assert.Null((await ReloadApplicationAsync(seed.ApplicationId)).CvDocumentId);
        Assert.False(await DocumentExistsAsync(seed.Cv1.Id));
        Assert.False(await DocumentExistsAsync(seed.Cv2.Id));
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static SetApplicationCvHandler SetCvHandler(RecruitmentDbContext db, FakeAuditPublisher audit) =>
        new(db, new FakeClock(FixedUtcNow), audit);

    private static SetApplicationCvRequest SetCvRequest(Seed seed, Guid cvDocumentId) => new()
    {
        CompanyId = seed.CompanyId,
        VacancyId = seed.VacancyId,
        ApplicationId = seed.ApplicationId,
        CvDocumentId = cvDocumentId,
        ExpectedVersion = 1,
    };

    private static DeleteCandidateDocumentHandler DeleteHandler(RecruitmentDbContext db, FakeCandidateDocumentStorageService storage) =>
        new(db, storage, new FakeClock(FixedUtcNow), new RecordingBackgroundJobClient(),
            NullLogger<DeleteCandidateDocumentHandler>.Instance);

    /// <summary>
    /// Fires once, immediately before the first command that deletes from candidate_documents is
    /// sent: commits (on a separate connection, outside the handler's transaction) an UPDATE making
    /// the application reference the CV being deleted, simulating a reference that appears between
    /// DeleteCandidateDocumentHandler's pre-check and its save.
    /// </summary>
    private sealed class AttachReferenceBeforeDeleteInterceptor(string connectionString, Guid applicationId, Guid documentId)
        : DbCommandInterceptor
    {
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await InjectIfDeleteAsync(command, cancellationToken);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await InjectIfDeleteAsync(command, cancellationToken);
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private async Task InjectIfDeleteAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText;
            if (!text.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("candidate_documents", StringComparison.OrdinalIgnoreCase))
                return;

            if (Interlocked.Exchange(ref _fired, 1) == 1)
                return;

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE recruitment.applications SET cv_document_id = @documentId WHERE id = @applicationId";
            update.Parameters.AddWithValue("documentId", documentId);
            update.Parameters.AddWithValue("applicationId", applicationId);
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
                throw new InvalidOperationException($"Expected to attach the racing CV reference to 1 application but affected {affected}.");
        }
    }
}
