using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DownloadCandidateDocument;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class DownloadCandidateDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_DownloadUrl_Derived_From_StorageKey()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "storage/key/resume.pdf", Guid.NewGuid(), Now);
        // [P1] Only a document whose malware scan is Clean is downloadable.
        document.BeginScanAttempt(Now);
        document.MarkScanClean(Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(document.StorageKey, result.Value!.ToString());
        Assert.Equal(new[] { document.StorageKey }, storage.DownloadUrlRequests);
    }

    // ── [P1] Malware-scan gating ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Pending), "document_scan_pending")]
    [InlineData(nameof(CandidateDocumentScanStatus.Scanning), "document_scan_pending")]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected), "document_quarantined")]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed), "document_scan_failed")]
    public async Task HandleAsync_Refuses_Non_Clean_Document_Without_Requesting_A_Download_Url(
        string statusName, string expectedCode)
    {
        var status = Enum.Parse<CandidateDocumentScanStatus>(statusName);
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "storage/key/resume.pdf", Guid.NewGuid(), Now);
        DriveTo(document, status);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.DoesNotContain(document.StorageKey, result.Error.Message);
        Assert.Empty(storage.DownloadUrlRequests);
    }

    [Fact]
    public void Error_Codes_Match_The_Endpoint_Contract()
    {
        Assert.Equal("document_scan_pending", DownloadCandidateDocumentHandler.ScanPendingCode);
        Assert.Equal("document_quarantined", DownloadCandidateDocumentHandler.QuarantinedCode);
        Assert.Equal("document_scan_failed", DownloadCandidateDocumentHandler.ScanFailedCode);
    }

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Pending), "document_scan_pending")]
    [InlineData(nameof(CandidateDocumentScanStatus.Scanning), "document_scan_pending")]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected), "document_quarantined")]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed), "document_scan_failed")]
    public void CheckDownloadable_Blocks_Every_Non_Clean_Status(string statusName, string expectedCode)
    {
        var status = Enum.Parse<CandidateDocumentScanStatus>(statusName);
        var error = DownloadCandidateDocumentHandler.CheckDownloadable(status);

        Assert.NotNull(error);
        Assert.Equal(expectedCode, error!.Code);
    }

    [Fact]
    public void CheckDownloadable_Allows_Only_Clean()
    {
        Assert.Null(DownloadCandidateDocumentHandler.CheckDownloadable(CandidateDocumentScanStatus.Clean));
        Assert.All(
            Enum.GetValues<CandidateDocumentScanStatus>().Where(s => s != CandidateDocumentScanStatus.Clean),
            s => Assert.NotNull(DownloadCandidateDocumentHandler.CheckDownloadable(s)));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Non_Clean_Document_In_Another_Company_Without_Leaking_Scan_State()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "k", Guid.NewGuid(), Now);
        DriveTo(document, CandidateDocumentScanStatus.Infected);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = Guid.NewGuid(), CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(storage.DownloadUrlRequests);
    }

    private static void DriveTo(CandidateDocument document, CandidateDocumentScanStatus status)
    {
        switch (status)
        {
            case CandidateDocumentScanStatus.Pending:
                break;
            case CandidateDocumentScanStatus.Scanning:
                document.BeginScanAttempt(Now);
                break;
            case CandidateDocumentScanStatus.Clean:
                document.BeginScanAttempt(Now);
                document.MarkScanClean(Now);
                break;
            case CandidateDocumentScanStatus.Infected:
                document.BeginScanAttempt(Now);
                document.MarkScanInfected("Eicar-Test-Signature", Now);
                break;
            case CandidateDocumentScanStatus.Failed:
                for (var i = 0; i < CandidateDocument.MaxScanAttempts; i++)
                {
                    document.BeginScanAttempt(Now);
                    document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now, Now);
                }
                break;
        }

        Assert.Equal(status, document.ScanStatus);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Missing()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = Guid.NewGuid(), CandidateId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Candidate()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "key", Guid.NewGuid(), Now);
        db.Candidates.AddRange(candidate, otherCandidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = companyId, CandidateId = otherCandidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
