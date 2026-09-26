using System.Net.Sockets;
using System.Text;
using Hangfire.States;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DownloadCandidateDocument;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// [P1] ScanCandidateDocumentJob: reads the stored bytes server-side (never via a signed URL), scans
/// them with the shared IUploadedFileScanner and records Clean / Infected (+ durable quarantine) /
/// failed attempt (+ bounded retry) — never Clean on error.
/// </summary>
public class ScanCandidateDocumentJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow);

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly FakeCandidateDocumentStorageService _storage = new();
    private readonly RecordingBackgroundJobClient _jobs = new();
    private readonly FakeAuditPublisher _audit = new();

    private RecruitmentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .Options);

    private async Task RunAsync(Guid documentId, IUploadedFileScanner scanner, DateTimeOffset? at = null)
    {
        await using var db = NewContext();
        var job = new ScanCandidateDocumentJob(
            db, _storage, scanner, _jobs, _audit,
            new FakeClock((at ?? Now).UtcDateTime),
            NullLogger<ScanCandidateDocumentJob>.Instance);
        await job.ScanAsync(documentId);
    }

    private async Task<CandidateDocument> SeedAsync(
        byte[]? content,
        string fileName = "cv.pdf",
        string contentType = "application/pdf",
        Action<CandidateDocument>? arrange = null)
    {
        var companyId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var storageKey = $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}";
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, "CV", fileName, content?.LongLength ?? 1,
            contentType, storageKey, Guid.NewGuid(), Now.AddMinutes(-1), CandidateDocumentKind.Cv);
        arrange?.Invoke(document);

        if (content is not null)
            _storage.Contents[storageKey] = content;

        await using var db = NewContext();
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private async Task<CandidateDocument> ReloadAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private async Task<List<CandidateDocumentDeletionOperation>> DeletionOperationsAsync()
    {
        await using var db = NewContext();
        return await db.CandidateDocumentDeletionOperations.AsNoTracking().ToListAsync();
    }

    private static byte[] HarmlessPdf => Encoding.ASCII.GetBytes("%PDF-1.7\nJane Doe - Curriculum Vitae\n%%EOF");

    private List<(Hangfire.Common.Job Job, IState State)> JobsOf<T>() =>
        _jobs.CreatedJobs.Select((j, i) => (j, _jobs.CreatedStates[i])).Where(x => x.j.Type == typeof(T)).ToList();

    // ── Clean ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clean_Content_Is_Marked_Clean_And_Audited_Without_Minting_A_Signed_Url()
    {
        var document = await SeedAsync(HarmlessPdf);
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Clean, saved.ScanStatus);
        Assert.True(saved.IsDownloadable);
        Assert.Equal(1, saved.ScanAttemptCount);
        Assert.Equal(Now, saved.ScanCompletedAt);
        Assert.Null(saved.ScanFailureReason);

        // The scanner saw exactly the stored bytes, read server-side.
        var scan = Assert.Single(scanner.Scans);
        Assert.Equal(HarmlessPdf, scan.Content);
        Assert.Empty(_storage.DownloadUrlRequests);

        var audit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(document.Id, audit.DocumentId);
        Assert.Equal(document.CompanyId, audit.CompanyId);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Pending), audit.PreviousStatus);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Clean), audit.NewStatus);
        Assert.Equal(1, audit.AttemptCount);

        Assert.Empty(await DeletionOperationsAsync());
        Assert.Empty(_jobs.CreatedJobs);
        Assert.Empty(_storage.Deletions);
    }

    // ── Infected / quarantine ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eicar_Content_Is_Infected_Quarantined_Audited_And_Purge_Enqueued()
    {
        var document = await SeedAsync(EicarTestFile.Bytes);

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Infected, saved.ScanStatus);
        Assert.False(saved.IsDownloadable);
        Assert.Equal(EicarTestFile.ThreatName, saved.ScanFailureReason);
        Assert.Equal(Now, saved.ScanCompletedAt);

        // Durable deletion intent committed with the Infected status.
        var operation = Assert.Single(await DeletionOperationsAsync());
        Assert.Equal(document.StorageKey, operation.StorageKey);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusPending, operation.Status);
        Assert.Equal(document.CompanyId, operation.CompanyId);
        Assert.Equal(document.CandidateId, operation.CandidateId);

        var statusAudit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(nameof(CandidateDocumentScanStatus.Scanning), statusAudit.PreviousStatus);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Infected), statusAudit.NewStatus);
        Assert.Equal(EicarTestFile.ThreatName, statusAudit.Reason);

        var quarantined = Assert.Single(_audit.Published.OfType<CandidateDocumentQuarantinedAuditEvent>());
        Assert.Equal(document.Id, quarantined.DocumentId);
        Assert.Equal(operation.Id, quarantined.DeletionOperationId);
        Assert.Equal(EicarTestFile.ThreatName, quarantined.ThreatName);

        var purge = Assert.Single(JobsOf<PurgeCandidateDocumentStorageJob>());
        Assert.Equal(operation.Id, (Guid)purge.Job.Args[0]!);

        // The job itself never deletes nor mints a URL — deletion is the durable pipeline's job.
        Assert.Empty(_storage.DownloadUrlRequests);
        Assert.Empty(_storage.Deletions);
    }

    [Fact]
    public async Task Download_Of_A_Quarantined_Document_Is_Refused_Without_Requesting_A_Url()
    {
        var document = await SeedAsync(EicarTestFile.Bytes);
        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        await using var db = NewContext();
        var result = await new DownloadCandidateDocumentHandler(db, _storage).HandleAsync(
            new DownloadCandidateDocumentRequest
            {
                CompanyId = document.CompanyId, CandidateId = document.CandidateId, DocumentId = document.Id,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("document_quarantined", result.Error.Code);
        Assert.Empty(_storage.DownloadUrlRequests);
    }

    [Fact]
    public async Task Eicar_Bytes_Spoofed_As_Pdf_Are_Still_Infected()
    {
        // Declared name/content type say PDF; the bytes are the EICAR test file.
        var document = await SeedAsync(EicarTestFile.Bytes, fileName: "curriculum-vitae.pdf", contentType: "application/pdf");
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Infected, saved.ScanStatus);
        Assert.Equal(EicarTestFile.Bytes, Assert.Single(scanner.Scans).Content);
    }

    [Fact]
    public async Task Eicar_Bytes_Embedded_In_A_Pdf_Looking_File_Are_Still_Infected()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n" + EicarTestFile.Signature + "\n%%EOF");
        var document = await SeedAsync(bytes, fileName: "cv.docx",
            contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        Assert.Equal(CandidateDocumentScanStatus.Infected, (await ReloadAsync(document.Id)).ScanStatus);
    }

    [Fact]
    public async Task Harmless_File_With_Mismatched_Metadata_Is_Clean_Only_Because_The_Scanner_Says_So()
    {
        // Name/type claim a Word document, bytes are a plain PDF: the job does not second-guess
        // metadata either way — the scanner's verdict alone decides.
        var document = await SeedAsync(HarmlessPdf, fileName: "cv.docx",
            contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        Assert.Equal(CandidateDocumentScanStatus.Clean, (await ReloadAsync(document.Id)).ScanStatus);
    }

    [Fact]
    public async Task Harmless_Bytes_Are_Quarantined_When_The_Scanner_Reports_Infected()
    {
        var document = await SeedAsync(HarmlessPdf);

        await RunAsync(document.Id, new FixedVerdictUploadedFileScanner(UploadedFileScanResult.Infected("Win.Trojan.Agent-123")));

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Infected, saved.ScanStatus);
        Assert.Equal("Win.Trojan.Agent-123", saved.ScanFailureReason);
    }

    [Fact]
    public async Task Unsafe_Threat_Name_From_Scanner_Is_Persisted_And_Audited_As_Unknown_Threat()
    {
        var document = await SeedAsync(EicarTestFile.Bytes);

        await RunAsync(document.Id, new FixedVerdictUploadedFileScanner(
            UploadedFileScanResult.Infected("<img src=x onerror=alert(1)> /tmp/uploads/secret.pdf?token=abc")));

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Infected, saved.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.UnknownThreat, saved.ScanFailureReason);
        Assert.Equal(CandidateDocumentScanFailureReasons.UnknownThreat,
            Assert.Single(_audit.Published.OfType<CandidateDocumentQuarantinedAuditEvent>()).ThreatName);
    }

    // ── Scanner outage ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scanner_Outage_Is_A_Failed_Attempt_Not_Clean_With_Retry_Scheduled_And_Audited()
    {
        var document = await SeedAsync(HarmlessPdf);
        var scanner = new ThrowingUploadedFileScanner(() =>
            new SocketException((int)SocketError.ConnectionRefused));

        await RunAsync(document.Id, scanner);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Pending, saved.ScanStatus);
        Assert.False(saved.IsDownloadable);
        Assert.Equal(1, saved.ScanAttemptCount);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, saved.ScanFailureReason);
        Assert.Equal(Now + ScanCandidateDocumentJob.RetryDelayAfter(1), saved.ScanNextAttemptAt);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), saved.ScanNextAttemptAt);

        var retryAudit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanRetryScheduledAuditEvent>());
        Assert.Equal(document.Id, retryAudit.DocumentId);
        Assert.Equal(1, retryAudit.FailedAttempt);
        Assert.Equal(CandidateDocument.MaxScanAttempts, retryAudit.MaxAttempts);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, retryAudit.Reason);
        Assert.Equal(saved.ScanNextAttemptAt, retryAudit.NextAttemptAt);
        Assert.Empty(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());

        var retry = Assert.Single(JobsOf<ScanCandidateDocumentJob>());
        Assert.Equal(document.Id, (Guid)retry.Job.Args[0]!);
        Assert.IsType<ScheduledState>(retry.State);

        Assert.Empty(_storage.DownloadUrlRequests);
        Assert.Empty(await DeletionOperationsAsync());
    }

    [Fact]
    public async Task Raw_Exception_Text_Is_Never_Persisted_Or_Audited()
    {
        const string secret = "https://storage.example/object/sign/cv.pdf?token=super-secret jane.doe@example.com";
        var document = await SeedAsync(HarmlessPdf);

        await RunAsync(document.Id, new ThrowingUploadedFileScanner(() => new InvalidOperationException(secret)));

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanFailureReasons.GenericFailure, saved.ScanFailureReason);
        var retryAudit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanRetryScheduledAuditEvent>());
        Assert.Equal(CandidateDocumentScanFailureReasons.GenericFailure, retryAudit.Reason);
        Assert.DoesNotContain("token", saved.ScanFailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Five_Failed_Attempts_End_Failed_Audited_And_Throw_Never_Clean()
    {
        var document = await SeedAsync(HarmlessPdf);
        var scanner = new ThrowingUploadedFileScanner(() => new SocketException((int)SocketError.HostUnreachable));

        // Attempts 1-4: each returns to Pending with a back-off; run each once it is due.
        var at = Now;
        for (var attempt = 1; attempt < CandidateDocument.MaxScanAttempts; attempt++)
        {
            await RunAsync(document.Id, scanner, at);

            var afterAttempt = await ReloadAsync(document.Id);
            Assert.Equal(CandidateDocumentScanStatus.Pending, afterAttempt.ScanStatus);
            Assert.Equal(attempt, afterAttempt.ScanAttemptCount);
            Assert.Equal(at + ScanCandidateDocumentJob.RetryDelayAfter(attempt), afterAttempt.ScanNextAttemptAt);
            at = afterAttempt.ScanNextAttemptAt!.Value;
        }

        // Attempt 5: terminally Failed, audited, and the job fails visibly.
        var ex = await Assert.ThrowsAsync<CandidateDocumentScanFailedException>(() => RunAsync(document.Id, scanner, at));
        Assert.IsType<SocketException>(ex.InnerException);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Failed, saved.ScanStatus);
        Assert.Equal(CandidateDocument.MaxScanAttempts, saved.ScanAttemptCount);
        Assert.False(saved.IsDownloadable);
        Assert.Null(saved.ScanNextAttemptAt);
        Assert.Equal(at, saved.ScanCompletedAt);
        Assert.Equal(CandidateDocument.MaxScanAttempts, scanner.Calls);

        Assert.Equal(CandidateDocument.MaxScanAttempts - 1,
            _audit.Published.OfType<CandidateDocumentScanRetryScheduledAuditEvent>().Count());
        var failedAudit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(nameof(CandidateDocumentScanStatus.Failed), failedAudit.NewStatus);
        Assert.Equal(CandidateDocument.MaxScanAttempts, failedAudit.AttemptCount);
        Assert.DoesNotContain(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>(),
            e => e.NewStatus == nameof(CandidateDocumentScanStatus.Clean));

        // Only the four retries were scheduled — no retry after the terminal failure.
        Assert.Equal(CandidateDocument.MaxScanAttempts - 1, JobsOf<ScanCandidateDocumentJob>().Count);

        // A further run after the terminal failure is a no-op.
        await RunAsync(document.Id, scanner, at.AddDays(1));
        Assert.Equal(CandidateDocument.MaxScanAttempts, scanner.Calls);
        Assert.Equal(CandidateDocumentScanStatus.Failed, (await ReloadAsync(document.Id)).ScanStatus);
    }

    [Fact]
    public async Task A_Recovered_Scanner_After_Failed_Attempts_Marks_Clean()
    {
        var document = await SeedAsync(HarmlessPdf);
        await RunAsync(document.Id, new ThrowingUploadedFileScanner(() => new TimeoutException()));
        var next = (await ReloadAsync(document.Id)).ScanNextAttemptAt!.Value;

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner(), next);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Clean, saved.ScanStatus);
        Assert.Equal(2, saved.ScanAttemptCount);
        Assert.Null(saved.ScanFailureReason);
    }

    // ── Storage outage / missing blob ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Storage_Read_Failure_Is_A_Failed_Attempt_And_The_Scanner_Is_Not_Called()
    {
        var document = await SeedAsync(HarmlessPdf);
        _storage.ThrowOnNextOpenReadAttempts = 1;
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);

        var saved = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Pending, saved.ScanStatus);
        Assert.Equal(1, saved.ScanAttemptCount);
        Assert.Equal(CandidateDocumentScanFailureReasons.FileUnreadable, saved.ScanFailureReason);
        Assert.NotNull(saved.ScanNextAttemptAt);
        Assert.Empty(scanner.Scans);
        Assert.Empty(_storage.DownloadUrlRequests);
    }

    [Fact]
    public async Task Missing_Blob_Is_A_Failed_Attempt_Not_Clean()
    {
        var document = await SeedAsync(content: null);

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        var saved = await ReloadAsync(document.Id);
        Assert.NotEqual(CandidateDocumentScanStatus.Clean, saved.ScanStatus);
        Assert.Equal(CandidateDocumentScanStatus.Pending, saved.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.FileUnreadable, saved.ScanFailureReason);
    }

    // ── Idempotency / guards ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Clean))]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected))]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed))]
    public async Task Running_On_A_Terminal_Document_Does_Nothing(string terminalName)
    {
        var terminal = Enum.Parse<CandidateDocumentScanStatus>(terminalName);
        var document = await SeedAsync(EicarTestFile.Bytes, arrange: d =>
        {
            switch (terminal)
            {
                case CandidateDocumentScanStatus.Clean:
                    d.BeginScanAttempt(Now.AddHours(-1));
                    d.MarkScanClean(Now.AddHours(-1));
                    break;
                case CandidateDocumentScanStatus.Infected:
                    d.BeginScanAttempt(Now.AddHours(-1));
                    d.MarkScanInfected(EicarTestFile.ThreatName, Now.AddHours(-1));
                    break;
                default:
                    for (var i = 0; i < CandidateDocument.MaxScanAttempts; i++)
                    {
                        var t = Now.AddHours(-10 + i);
                        d.BeginScanAttempt(t);
                        d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, t, t);
                    }
                    break;
            }
        });
        var before = await ReloadAsync(document.Id);
        var scanner = new FixedVerdictUploadedFileScanner(UploadedFileScanResult.Clean());

        await RunAsync(document.Id, scanner);

        var after = await ReloadAsync(document.Id);
        Assert.Equal(terminal, after.ScanStatus);
        Assert.Equal(before.ScanAttemptCount, after.ScanAttemptCount);
        Assert.Equal(before.ScanCompletedAt, after.ScanCompletedAt);
        Assert.Equal(0, scanner.Calls);
        Assert.Empty(_audit.Published);
        Assert.Empty(_jobs.CreatedJobs);
        Assert.Empty(await DeletionOperationsAsync());
    }

    [Fact]
    public async Task Running_A_Second_Time_After_Clean_Does_Not_Rescan()
    {
        var document = await SeedAsync(HarmlessPdf);
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);
        await RunAsync(document.Id, scanner, Now.AddMinutes(1));

        Assert.Single(scanner.Scans);
        Assert.Single(_audit.Published);
        Assert.Equal(1, (await ReloadAsync(document.Id)).ScanAttemptCount);
    }

    [Fact]
    public async Task Running_A_Second_Time_After_Infected_Does_Not_Create_A_Second_Deletion_Operation()
    {
        var document = await SeedAsync(EicarTestFile.Bytes);
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);
        await RunAsync(document.Id, scanner, Now.AddMinutes(1));

        Assert.Single(scanner.Scans);
        Assert.Single(await DeletionOperationsAsync());
        Assert.Single(JobsOf<PurgeCandidateDocumentStorageJob>());
    }

    [Fact]
    public async Task Running_During_Back_Off_Does_Nothing()
    {
        var document = await SeedAsync(HarmlessPdf);
        await RunAsync(document.Id, new ThrowingUploadedFileScanner(() => new SocketException()));
        var afterFailure = await ReloadAsync(document.Id);
        var auditCount = _audit.Published.Count;
        var jobCount = _jobs.CreatedJobs.Count;
        var scanner = new EicarDetectingUploadedFileScanner();

        // One tick before the back-off elapses.
        await RunAsync(document.Id, scanner, afterFailure.ScanNextAttemptAt!.Value.AddTicks(-1));

        var after = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Pending, after.ScanStatus);
        Assert.Equal(1, after.ScanAttemptCount);
        Assert.Empty(scanner.Scans);
        Assert.Equal(auditCount, _audit.Published.Count);
        Assert.Equal(jobCount, _jobs.CreatedJobs.Count);
    }

    [Fact]
    public async Task Running_While_Another_Worker_Holds_A_Live_Claim_Does_Nothing()
    {
        var document = await SeedAsync(HarmlessPdf, arrange: d => d.BeginScanAttempt(Now.AddMinutes(-1)));
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);

        var after = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Scanning, after.ScanStatus);
        Assert.Equal(1, after.ScanAttemptCount);
        Assert.Empty(scanner.Scans);
    }

    [Fact]
    public async Task Running_After_Another_Workers_Claim_Expired_Takes_Over_And_Scans()
    {
        var document = await SeedAsync(HarmlessPdf,
            arrange: d => d.BeginScanAttempt(Now - CandidateDocument.ScanLeaseDuration));

        await RunAsync(document.Id, new EicarDetectingUploadedFileScanner());

        var after = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Clean, after.ScanStatus);
        Assert.Equal(2, after.ScanAttemptCount);
        var audit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(nameof(CandidateDocumentScanStatus.Scanning), audit.PreviousStatus);
    }

    [Fact]
    public async Task Pending_Document_With_Exhausted_Attempts_Is_Closed_Off_As_Failed_Without_Scanning()
    {
        var document = await SeedAsync(HarmlessPdf, arrange: d =>
            typeof(CandidateDocument).GetProperty(nameof(CandidateDocument.ScanAttemptCount))!
                .SetValue(d, CandidateDocument.MaxScanAttempts));
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(document.Id, scanner);

        var after = await ReloadAsync(document.Id);
        Assert.Equal(CandidateDocumentScanStatus.Failed, after.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.RetryLimitReached, after.ScanFailureReason);
        Assert.Empty(scanner.Scans);
        var audit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(nameof(CandidateDocumentScanStatus.Pending), audit.PreviousStatus);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Failed), audit.NewStatus);
    }

    [Fact]
    public async Task Missing_Document_Is_A_No_Op()
    {
        var scanner = new EicarDetectingUploadedFileScanner();

        await RunAsync(Guid.NewGuid(), scanner);

        Assert.Empty(scanner.Scans);
        Assert.Empty(_audit.Published);
        Assert.Empty(_jobs.CreatedJobs);
        Assert.Empty(_storage.DownloadUrlRequests);
    }

    // ── Best-effort side effects never undo the recorded outcome ─────────────────────────────

    [Fact]
    public async Task Job_Store_Outage_Does_Not_Undo_The_Quarantine()
    {
        var document = await SeedAsync(EicarTestFile.Bytes);

        await using (var db = NewContext())
        {
            var job = new ScanCandidateDocumentJob(
                db, _storage, new EicarDetectingUploadedFileScanner(), new ThrowingBackgroundJobClient(), _audit,
                new FakeClock(FixedUtcNow), NullLogger<ScanCandidateDocumentJob>.Instance);
            await job.ScanAsync(document.Id);
        }

        Assert.Equal(CandidateDocumentScanStatus.Infected, (await ReloadAsync(document.Id)).ScanStatus);
        Assert.Single(await DeletionOperationsAsync());
    }

    [Fact]
    public async Task Audit_Outage_Does_Not_Undo_The_Clean_Result()
    {
        var document = await SeedAsync(HarmlessPdf);

        await using (var db = NewContext())
        {
            var job = new ScanCandidateDocumentJob(
                db, _storage, new EicarDetectingUploadedFileScanner(), _jobs, new ThrowingAuditPublisher(),
                new FakeClock(FixedUtcNow), NullLogger<ScanCandidateDocumentJob>.Instance);
            await job.ScanAsync(document.Id);
        }

        Assert.Equal(CandidateDocumentScanStatus.Clean, (await ReloadAsync(document.Id)).ScanStatus);
    }

    private sealed class ThrowingAuditPublisher : HR.SharedKernel.IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit sink outage.");
    }
}
