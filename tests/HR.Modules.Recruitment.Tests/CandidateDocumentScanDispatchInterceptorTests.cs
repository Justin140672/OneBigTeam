using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// [P1] CandidateDocumentScanDispatchInterceptor enqueues ScanCandidateDocumentJob for every newly
/// inserted CandidateDocument once the insert is saved. The InMemory provider has no real
/// transactions, so only the no-ambient-transaction path (SaveChanges is the commit) is covered here;
/// the transaction-commit path is covered by CandidateDocumentScanGatingEndpointTests in
/// HR.Integration.Tests against Postgres.
/// </summary>
public class CandidateDocumentScanDispatchInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    private RecruitmentDbContext NewContext(IBackgroundJobClient jobClient) =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .AddInterceptors(new CandidateDocumentScanDispatchInterceptor(
                jobClient, NullLogger<CandidateDocumentScanDispatchInterceptor>.Instance))
            .Options);

    private static CandidateDocument NewDocument(Guid? candidateId = null) =>
        CandidateDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), candidateId ?? Guid.NewGuid(), "CV", "cv.pdf", 1024, "application/pdf",
            $"key/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);

    private static List<Guid> ScanJobIds(RecordingBackgroundJobClient client) =>
        client.CreatedJobs
            .Where(j => j.Type == typeof(ScanCandidateDocumentJob) && j.Method.Name == nameof(ScanCandidateDocumentJob.ScanAsync))
            .Select(j => (Guid)j.Args[0]!)
            .ToList();

    [Fact]
    public async Task SaveChangesAsync_Adding_A_Document_Enqueues_Exactly_One_Scan_For_Its_Id()
    {
        var client = new RecordingBackgroundJobClient();
        await using var db = NewContext(client);
        var document = NewDocument();
        db.CandidateDocuments.Add(document);

        await db.SaveChangesAsync();

        Assert.Equal(new[] { document.Id }, ScanJobIds(client));
        Assert.Single(client.CreatedJobs);
        Assert.IsType<Hangfire.States.EnqueuedState>(Assert.Single(client.CreatedStates));
    }

    [Fact]
    public void SaveChanges_Sync_Adding_A_Document_Enqueues_A_Scan()
    {
        var client = new RecordingBackgroundJobClient();
        using var db = NewContext(client);
        var document = NewDocument();
        db.CandidateDocuments.Add(document);

        db.SaveChanges();

        Assert.Equal(new[] { document.Id }, ScanJobIds(client));
    }

    [Fact]
    public async Task Adding_Several_Documents_In_One_Save_Enqueues_One_Scan_Each()
    {
        var client = new RecordingBackgroundJobClient();
        await using var db = NewContext(client);
        var a = NewDocument();
        var b = NewDocument();
        db.CandidateDocuments.AddRange(a, b);

        await db.SaveChangesAsync();

        var ids = ScanJobIds(client);
        Assert.Equal(2, ids.Count);
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
    }

    [Fact]
    public async Task Saving_An_Unrelated_Change_Enqueues_Nothing()
    {
        var client = new RecordingBackgroundJobClient();
        await using var db = NewContext(client);
        db.Candidates.Add(Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now));

        await db.SaveChangesAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task Modifying_An_Existing_Document_Does_Not_Re_Enqueue_A_Scan()
    {
        var seedClient = new RecordingBackgroundJobClient();
        var document = NewDocument();
        await using (var seedDb = NewContext(seedClient))
        {
            seedDb.CandidateDocuments.Add(document);
            await seedDb.SaveChangesAsync();
        }

        var client = new RecordingBackgroundJobClient();
        await using var db = NewContext(client);
        var tracked = await db.CandidateDocuments.SingleAsync(d => d.Id == document.Id);
        tracked.BeginScanAttempt(Now);
        tracked.MarkScanClean(Now);

        await db.SaveChangesAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task A_Second_Save_On_The_Same_Context_Does_Not_Re_Dispatch_Earlier_Documents()
    {
        var client = new RecordingBackgroundJobClient();
        await using var db = NewContext(client);
        var first = NewDocument();
        db.CandidateDocuments.Add(first);
        await db.SaveChangesAsync();

        var second = NewDocument();
        db.CandidateDocuments.Add(second);
        await db.SaveChangesAsync();

        // Then a save with nothing new.
        await db.SaveChangesAsync();

        Assert.Equal(new[] { first.Id, second.Id }, ScanJobIds(client));
    }

    [Fact]
    public async Task A_Throwing_Job_Client_Does_Not_Fail_SaveChanges_And_The_Document_Is_Persisted_Pending()
    {
        var client = new ThrowingBackgroundJobClient();
        var document = NewDocument();
        await using (var db = NewContext(client))
        {
            db.CandidateDocuments.Add(document);

            var written = await db.SaveChangesAsync();

            Assert.Equal(1, written);
            Assert.Equal(1, client.Attempts);
        }

        await using var verify = NewContext(new RecordingBackgroundJobClient());
        var saved = await verify.CandidateDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        // Still Pending, so ReconcileCandidateDocumentScansJob will dispatch it later.
        Assert.Equal(CandidateDocumentScanStatus.Pending, saved.ScanStatus);
    }
}
