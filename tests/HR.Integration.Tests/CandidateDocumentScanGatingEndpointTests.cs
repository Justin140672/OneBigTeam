using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CandidateDocumentScanGatingEndpointTests
{
    private const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000f5-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CandidateDocumentScanGatingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static string DownloadUrl(Guid companyId, Guid candidateId, Guid documentId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents/{documentId}/download";

    private static string DocumentsUrl(Guid companyId, Guid candidateId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents";

    private FakeBackgroundJobClient Jobs =>
        (FakeBackgroundJobClient)_factory.Services.GetRequiredService<IBackgroundJobClient>();

    private int ScanJobsFor(Guid documentId) =>
        Jobs.CreatedJobs.Count(j =>
            j.Type == typeof(ScanCandidateDocumentJob)
            && j.Method.Name == nameof(ScanCandidateDocumentJob.ScanAsync)
            && j.Args.Count == 1
            && j.Args[0] is Guid id && id == documentId);

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string fileName, string contentType, string title = "CV")
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent("Cv"), "Kind");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(file, "File", fileName);
        return content;
    }

    private static byte[] HarmlessPdf =>
        Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\nJane Doe - Curriculum Vitae\n%%EOF");

    private async Task<Guid> UploadAsync(HttpClient client, Guid companyId, Guid candidateId, byte[] bytes, string fileName = "cv.pdf", string contentType = "application/pdf")
    {
        var response = await client.PostAsync(DocumentsUrl(companyId, candidateId), BuildUpload(bytes, fileName, contentType));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Pending", payload!.ScanStatus);
        return payload.Id;
    }

    private async Task RunScanWithHostScannerAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ScanCandidateDocumentJob>().ScanAsync(documentId);
    }

    private async Task RunScanWithAsync(Guid documentId, IUploadedFileScanner scanner)
    {
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<ScanCandidateDocumentJob>(scope.ServiceProvider, scanner);
        await job.ScanAsync(documentId);
    }

    private async Task<CandidateDocument> LoadDocumentAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId);
    }

    private static async Task AssertBlockedAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(body);
        Assert.Equal(expectedCode, body!.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Error));
    }


    [Fact]
    public async Task Get_Download_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(DownloadUrl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Download_Returns_Conflict_For_Pending_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(_factory, companyId, candidateId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));

        await AssertBlockedAsync(response, HttpStatusCode.Conflict, "document_scan_pending");
    }

    [Fact]
    public async Task Get_Download_Returns_Conflict_For_Scanning_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, DateTimeOffset.UtcNow, scanStatus: CandidateDocumentScanStatus.Scanning);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));

        await AssertBlockedAsync(response, HttpStatusCode.Conflict, "document_scan_pending");
    }

    [Fact]
    public async Task Get_Download_Returns_Forbidden_Quarantined_For_Infected_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, Now, scanStatus: CandidateDocumentScanStatus.Infected);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));

        await AssertBlockedAsync(response, HttpStatusCode.Forbidden, "document_quarantined");
    }

    [Fact]
    public async Task Get_Download_Returns_Forbidden_Scan_Failed_For_Failed_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, Now, scanStatus: CandidateDocumentScanStatus.Failed);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));

        await AssertBlockedAsync(response, HttpStatusCode.Forbidden, "document_scan_failed");
    }

    [Fact]
    public async Task Get_Download_Redirects_For_Clean_Document()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, Now, scanStatus: CandidateDocumentScanStatus.Clean);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Get_Download_Returns_NotFound_For_Clean_Document_In_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyA, candidateId, Now, scanStatus: CandidateDocumentScanStatus.Clean);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.GetAsync(DownloadUrl(companyB, candidateId, documentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Get_Download_Returns_NotFound_For_Infected_Document_In_Another_Company_Without_Leaking_Scan_State()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyA, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyA, candidateId, Now, scanStatus: CandidateDocumentScanStatus.Infected);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.GetAsync(DownloadUrl(companyB, candidateId, documentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_Documents_Reports_ScanStatus_And_Only_Clean_Is_Downloadable()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var expected = new Dictionary<Guid, CandidateDocumentScanStatus>();
        foreach (var status in Enum.GetValues<CandidateDocumentScanStatus>())
        {
            var id = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
                _factory, companyId, candidateId, Now, title: $"Doc {status}", scanStatus: status);
            expected[id] = status;
        }
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(DocumentsUrl(companyId, candidateId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(expected.Count, payload!.Items.Count);
        foreach (var (id, status) in expected)
        {
            var item = Assert.Single(payload.Items, i => i.Id == id);
            Assert.Equal(status.ToString(), item.ScanStatus);
            Assert.Equal(status == CandidateDocumentScanStatus.Clean, item.IsDownloadable);
        }
    }


    [Fact]
    public async Task Uploaded_Clean_Cv_Is_Blocked_Until_Scanned_Then_Downloadable()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var documentId = await UploadAsync(client, companyId, candidateId, HarmlessPdf);

        var before = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));
        await AssertBlockedAsync(before, HttpStatusCode.Conflict, "document_scan_pending");

        Assert.Equal(1, ScanJobsFor(documentId));

        await RunScanWithHostScannerAsync(documentId);

        var saved = await LoadDocumentAsync(documentId);
        Assert.Equal(CandidateDocumentScanStatus.Clean, saved.ScanStatus);
        Assert.Equal(1, saved.ScanAttemptCount);

        var after = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.NotNull(after.Headers.Location);
    }

    [Fact]
    public async Task Uploaded_Eicar_Spoofed_As_Pdf_Is_Quarantined_And_Never_Downloadable()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var spoofed = "%PDF-1.7\n" + Eicar + "\n%%EOF";
        var documentId = await UploadAsync(client, companyId, candidateId, Encoding.ASCII.GetBytes(spoofed), "cv.pdf", "application/pdf");

        var scanner = new EicarDetectingScanner();
        await RunScanWithAsync(documentId, scanner);

        Assert.Equal(spoofed, Assert.Single(scanner.ScannedContents));

        var saved = await LoadDocumentAsync(documentId);
        Assert.Equal(CandidateDocumentScanStatus.Infected, saved.ScanStatus);
        Assert.Equal("Eicar-Test-Signature", saved.ScanFailureReason);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var quarantine = await db.CandidateDocumentDeletionOperations.AsNoTracking()
                .Where(o => o.StorageKey == saved.StorageKey && o.Status != CandidateDocumentDeletionOperation.StatusReserved)
                .ToListAsync();
            var operation = Assert.Single(quarantine);
            Assert.Equal(companyId, operation.CompanyId);
            Assert.Equal(candidateId, operation.CandidateId);

            Assert.Contains(Jobs.CreatedJobs, j =>
                j.Type == typeof(PurgeCandidateDocumentStorageJob) && j.Args.Count > 0 && j.Args[0] is Guid g && g == operation.Id);
        }

        var download = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));
        await AssertBlockedAsync(download, HttpStatusCode.Forbidden, "document_quarantined");

        var list = await client.GetFromJsonAsync<ListPayload>(DocumentsUrl(companyId, candidateId));
        var item = Assert.Single(list!.Items, i => i.Id == documentId);
        Assert.Equal("Infected", item.ScanStatus);
        Assert.False(item.IsDownloadable);

        await RunScanWithHostScannerAsync(documentId);
        Assert.Equal(CandidateDocumentScanStatus.Infected, (await LoadDocumentAsync(documentId)).ScanStatus);
    }

    [Fact]
    public async Task Scanner_Outage_Leaves_Upload_Pending_Blocked_And_Never_Clean()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);
        var documentId = await UploadAsync(client, companyId, candidateId, HarmlessPdf);
        var scanJobsBefore = ScanJobsFor(documentId);

        await RunScanWithAsync(documentId, new ThrowingScanner());

        var saved = await LoadDocumentAsync(documentId);
        Assert.Equal(CandidateDocumentScanStatus.Pending, saved.ScanStatus);
        Assert.NotEqual(CandidateDocumentScanStatus.Clean, saved.ScanStatus);
        Assert.Equal(1, saved.ScanAttemptCount);
        Assert.NotNull(saved.ScanNextAttemptAt);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, saved.ScanFailureReason);

        Assert.Equal(scanJobsBefore + 1, ScanJobsFor(documentId));

        var download = await client.GetAsync(DownloadUrl(companyId, candidateId, documentId));
        await AssertBlockedAsync(download, HttpStatusCode.Conflict, "document_scan_pending");
    }


    [Fact]
    public async Task Reconciliation_Dispatches_A_Backfilled_Pending_Document_But_Not_A_Fresh_Upload()
    {
        var companyId = Guid.NewGuid();
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var backfilledId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), title: "Legacy CV");
        var freshId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, DateTimeOffset.UtcNow, title: "New CV");
        var backfilledBefore = ScanJobsFor(backfilledId);
        var freshBefore = ScanJobsFor(freshId);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReconcileCandidateDocumentScansJob>().ExecuteAsync();
        }

        Assert.Equal(backfilledBefore + 1, ScanJobsFor(backfilledId));
        Assert.Equal(freshBefore, ScanJobsFor(freshId));
        Assert.Equal(CandidateDocumentScanStatus.Pending, (await LoadDocumentAsync(backfilledId)).ScanStatus);

        await RunScanWithHostScannerAsync(backfilledId);
        var afterMissingBlob = await LoadDocumentAsync(backfilledId);
        Assert.Equal(CandidateDocumentScanStatus.Pending, afterMissingBlob.ScanStatus);
        Assert.Equal(1, afterMissingBlob.ScanAttemptCount);
        Assert.Equal(CandidateDocumentScanFailureReasons.FileUnreadable, afterMissingBlob.ScanFailureReason);

        await StoreBlobAndMakeRetryDueAsync(backfilledId, HarmlessPdf);
        await RunScanWithHostScannerAsync(backfilledId);
        var cleared = await LoadDocumentAsync(backfilledId);
        Assert.Equal(CandidateDocumentScanStatus.Clean, cleared.ScanStatus);
        Assert.Equal(2, cleared.ScanAttemptCount);
    }

    private async Task StoreBlobAndMakeRetryDueAsync(Guid documentId, byte[] content)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<HR.Modules.Recruitment.Services.ICandidateDocumentStorageService>();

        var document = await db.CandidateDocuments.SingleAsync(d => d.Id == documentId);
        await storage.UploadAsync(new MemoryStream(content), document.StorageKey, "application/pdf", CancellationToken.None);

        await db.CandidateDocuments
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ScanNextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }


    [Fact]
    public async Task New_Candidate_Application_With_Cv_Dispatches_A_Scan_After_Commit_And_Cv_Is_Blocked()
    {
        var companyId = Guid.NewGuid();
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Priya"), "FirstName" },
            { new StringContent("Shah"), "LastName" },
            { new StringContent($"priya.shah.{Guid.NewGuid():N}@example.com"), "Email" },
        };
        var cv = new ByteArrayContent(HarmlessPdf);
        cv.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(cv, "CvFile", "priya-cv.pdf");

        var response = await client.PostAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NewCandidateApplicationPayload>();
        Assert.NotNull(created);
        var documentId = Assert.IsType<Guid>(created!.CvDocumentId);

        Assert.Equal(1, ScanJobsFor(documentId));
        Assert.Equal(CandidateDocumentScanStatus.Pending, (await LoadDocumentAsync(documentId)).ScanStatus);

        var download = await client.GetAsync(DownloadUrl(companyId, created.CandidateId, documentId));
        await AssertBlockedAsync(download, HttpStatusCode.Conflict, "document_scan_pending");
    }


    private sealed class EicarDetectingScanner : IUploadedFileScanner
    {
        public List<string> ScannedContents { get; } = [];

        public async Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(content, Encoding.ASCII);
            var text = await reader.ReadToEndAsync(cancellationToken);
            ScannedContents.Add(text);
            return text.Contains(Eicar, StringComparison.Ordinal)
                ? UploadedFileScanResult.Infected("Eicar-Test-Signature")
                : UploadedFileScanResult.Clean();
        }
    }

    private sealed class ThrowingScanner : IUploadedFileScanner
    {
        public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            throw new SocketException((int)SocketError.ConnectionRefused);
    }

    private sealed record UploadPayload(Guid Id, Guid CandidateId, string FileName, string ScanStatus);

    private sealed record ListPayload(List<ListItem> Items);

    private sealed record ListItem(Guid Id, string Title, string ScanStatus, bool IsDownloadable);

    private sealed record ProblemPayload(string Error, string Code);

    private sealed record NewCandidateApplicationPayload(Guid CandidateId, Guid ApplicationId, Guid? CvDocumentId);
}
