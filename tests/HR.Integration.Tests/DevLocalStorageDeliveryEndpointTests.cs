using System.Net;
using System.Text;
using System.Web;
using HR.Api.Startup;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// [P2] The Development-only local-storage delivery route
/// (GET /api/dev/local-storage/{bucket}/{*key}) must make the same authorization and malware-scan
/// decisions as production: a file is only served for a short-lived HMAC-signed URL minted by an
/// authorised download handler, and only while the owning module re-confirms the record is live and
/// Clean. Every refusal is a 404. The physical file is written for every case, so each denial proves
/// the route refused a file that really exists on disk.
/// </summary>
[Collection("Integration")]
public class DevLocalStorageDeliveryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000f5-0000-0000-0000-00000000a001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public DevLocalStorageDeliveryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
        }).GetAwaiter().GetResult();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private ILocalStorageUrlSigner Signer => _factory.Services.GetRequiredService<ILocalStorageUrlSigner>();

    private static string DownloadUrl(Guid companyId, Guid candidateId, Guid documentId) =>
        $"/api/companies/{companyId}/candidates/{candidateId}/documents/{documentId}/download";

    private static string RawUrl(string bucket, string key) =>
        $"/api/dev/local-storage/{bucket}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";

    private static byte[] WriteFile(string bucket, string storageKey, string? content = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? $"%PDF-1.7 local-storage delivery test {Guid.NewGuid():N}");
        var path = Path.Combine(LocalStorageBuckets.GetRootPath(bucket), storageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    private async Task<(Guid CandidateId, Guid DocumentId, string StorageKey)> SeedCvAsync(
        Guid companyId, CandidateDocumentScanStatus scanStatus)
    {
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        var documentId = await RecruitmentTestSeeder.SeedCandidateDocumentAsync(
            _factory, companyId, candidateId, Now, scanStatus: scanStatus);
        return (candidateId, documentId, await LoadCvStorageKeyAsync(documentId));
    }

    private async Task<string> LoadCvStorageKeyAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.CandidateDocuments.Where(d => d.Id == documentId).Select(d => d.StorageKey).SingleAsync();
    }

    private async Task<Guid> SeedCvWithStorageKeyAsync(Guid companyId, string storageKey)
    {
        var candidateId = await RecruitmentTestSeeder.SeedCandidateAsync(_factory, companyId, Now);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, "CV", "cv.pdf", 2048, "application/pdf",
            storageKey, Guid.NewGuid(), Now, CandidateDocumentKind.Other);
        RecruitmentTestSeeder.ApplyScanStatus(document, CandidateDocumentScanStatus.Clean, DateTimeOffset.UtcNow);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    /// <summary>A recruiter obtains the dev signed URL through the normal authorised download handler.</summary>
    private async Task<Uri> GetSignedCvUrlAsync(HttpClient recruiter, Guid companyId, Guid candidateId, Guid documentId)
    {
        var response = await recruiter.GetAsync(DownloadUrl(companyId, candidateId, documentId));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith($"/api/dev/local-storage/{LocalStorageBuckets.CandidateDocuments}/", location.AbsolutePath);
        return location;
    }

    private static (string Exp, string Sig) ReadSignature(Uri url)
    {
        var query = HttpUtility.ParseQueryString(url.Query);
        return (query["exp"]!, query["sig"]!);
    }

    // ── happy path ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clean_Cv_Is_Downloadable_By_Authorised_Recruiter_Through_The_Signed_Local_Url()
    {
        var companyId = Guid.NewGuid();
        var (candidateId, documentId, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        var bytes = WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        using var recruiter = await ClientAs(RecruiterUser, companyId);

        var signedUrl = await GetSignedCvUrlAsync(recruiter, companyId, candidateId, documentId);
        var (exp, sig) = ReadSignature(signedUrl);
        Assert.False(string.IsNullOrEmpty(exp));
        Assert.False(string.IsNullOrEmpty(sig));

        // The browser (or HR.Web's CV proxy) follows the redirect without an API bearer token, exactly
        // as it would follow a Supabase signed URL.
        using var browser = AnonymousClient();
        var response = await browser.GetAsync(signedUrl.PathAndQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    // ── anonymous / unsigned access ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_Direct_Access_By_Bucket_And_Key_Is_Denied()
    {
        var companyId = Guid.NewGuid();
        var (_, _, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(RawUrl(LocalStorageBuckets.CandidateDocuments, key));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Same_Company_Recruiter_Cannot_Use_An_Unsigned_Key()
    {
        var companyId = Guid.NewGuid();
        var (_, _, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        using var recruiter = await ClientAs(RecruiterUser, companyId);

        var response = await recruiter.GetAsync(RawUrl(LocalStorageBuckets.CandidateDocuments, key));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── cross-company ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cross_Company_Recruiter_Cannot_Reuse_Own_Signature_On_Another_Companys_Copied_Key()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (_, _, keyA) = await SeedCvAsync(companyA, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, keyA);
        var (candidateB, documentB, keyB) = await SeedCvAsync(companyB, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, keyB);
        using var recruiterB = await ClientAs(RecruiterUser, companyB);

        // B legitimately obtains a signed URL for its own CV...
        var ownUrl = await GetSignedCvUrlAsync(recruiterB, companyB, candidateB, documentB);
        var (exp, sig) = ReadSignature(ownUrl);

        // ...then swaps in company A's copied storage key, and also tries A's key with no signature.
        var swapped = await recruiterB.GetAsync(
            $"{RawUrl(LocalStorageBuckets.CandidateDocuments, keyA)}?exp={exp}&sig={sig}");
        var unsigned = await recruiterB.GetAsync(RawUrl(LocalStorageBuckets.CandidateDocuments, keyA));

        Assert.Equal(HttpStatusCode.NotFound, swapped.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unsigned.StatusCode);
    }

    [Fact]
    public async Task Cross_Company_Recruiter_Cannot_Mint_A_Local_Url_For_Another_Companys_Cv()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (candidateA, documentA, keyA) = await SeedCvAsync(companyA, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, keyA);
        using var recruiterB = await ClientAs(RecruiterUser, companyB);

        var response = await recruiterB.GetAsync(DownloadUrl(companyB, candidateA, documentA));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    // ── scan state ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pending")]
    [InlineData("Scanning")]
    [InlineData("Infected")]
    [InlineData("Failed")]
    public async Task Non_Clean_Cv_Is_Denied_Even_With_A_Valid_Signature_And_An_Existing_File(string status)
    {
        var companyId = Guid.NewGuid();
        var (_, _, key) = await SeedCvAsync(companyId, Enum.Parse<CandidateDocumentScanStatus>(status));
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);

        // A validly-signed URL (e.g. one copied before the scan state changed) must still be refused.
        var signed = Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, key);
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(signed.PathAndQuery);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── orphaned files ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orphaned_File_With_No_Db_Record_Is_Denied_Even_With_A_Valid_Signature()
    {
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}/orphan.pdf";
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        var signed = Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, key);
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(signed.PathAndQuery);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signed_Url_Stops_Working_Once_The_Record_Is_Deleted()
    {
        var companyId = Guid.NewGuid();
        var (candidateId, documentId, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        using var recruiter = await ClientAs(RecruiterUser, companyId);
        var signedUrl = await GetSignedCvUrlAsync(recruiter, companyId, candidateId, documentId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            await db.CandidateDocuments.Where(d => d.Id == documentId).ExecuteDeleteAsync();
        }

        using var anonymous = AnonymousClient();
        var response = await anonymous.GetAsync(signedUrl.PathAndQuery);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── traversal / malformed input ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("..%2F..%2Fsecret.txt")]
    [InlineData("%2e%2e/%2e%2e/secret.txt")]
    [InlineData("%252e%252e/%252e%252e/secret.txt")]
    [InlineData("a/..%5C..%5Csecret.txt")]
    [InlineData("a/%2e%2e%5csecret.txt")]
    [InlineData("C:%5CWindows%5Cwin.ini")]
    [InlineData("a//b.pdf")]
    // (%00 itself is refused by the host's URL decoder before routing; NUL is covered by Key_Shape_Validation.)
    [InlineData("a/%01/b.pdf")]
    public async Task Encoded_Traversal_And_Malformed_Keys_Are_Rejected(string encodedKey)
    {
        // A secret just outside the bucket root, so a traversal bug would have something to leak.
        var secretPath = Path.Combine(
            Path.GetDirectoryName(LocalStorageBuckets.GetRootPath(LocalStorageBuckets.CandidateDocuments))!,
            "secret.txt");
        File.WriteAllText(secretPath, "TOP-SECRET-OUTSIDE-BUCKET");

        // Sign the decoded form too, so the refusal cannot be down to a bad signature alone.
        var decodedKey = Uri.UnescapeDataString(encodedKey);
        var (exp, sig) = ReadSignature(Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, decodedKey));
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(
            $"/api/dev/local-storage/{LocalStorageBuckets.CandidateDocuments}/{encodedKey}?exp={exp}&sig={sig}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("TOP-SECRET", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Traversal_Key_Is_Rejected_Even_When_A_Clean_Record_And_Valid_Signature_Exist()
    {
        // Worst case: a (hypothetical) record owns a traversal-shaped key and a valid signature exists.
        // The path defences must still refuse to leave the bucket root.
        var companyId = Guid.NewGuid();
        const string maliciousKey = "..%2Fsecret-record.txt";
        await SeedCvWithStorageKeyAsync(companyId, maliciousKey);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(LocalStorageBuckets.GetRootPath(LocalStorageBuckets.CandidateDocuments))!, "secret-record.txt"),
            "TOP-SECRET-RECORD");
        var (exp, sig) = ReadSignature(Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, maliciousKey));
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(
            $"/api/dev/local-storage/{LocalStorageBuckets.CandidateDocuments}/..%252Fsecret-record.txt?exp={exp}&sig={sig}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("TOP-SECRET", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("unknown-bucket")]
    [InlineData("Candidate-Documents")]
    [InlineData("..")]
    public async Task Unknown_Or_Differently_Cased_Bucket_Is_Rejected(string bucket)
    {
        var companyId = Guid.NewGuid();
        var (_, _, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        var (exp, sig) = ReadSignature(Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, key));
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync($"{RawUrl(bucket, key)}?exp={exp}&sig={sig}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("a/b/c.pdf", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e/5b1e/abc.pdf", true)]
    [InlineData("", false)]
    [InlineData("/a/b.pdf", false)]
    [InlineData("a/../b.pdf", false)]
    [InlineData("a/./b.pdf", false)]
    [InlineData("a//b.pdf", false)]
    [InlineData("a/%2e%2e/b.pdf", false)]
    [InlineData("..%2Fb.pdf", false)]
    [InlineData("a/b%5Cc.pdf", false)]
    [InlineData("a\\b.pdf", false)]
    [InlineData("C:/b.pdf", false)]
    [InlineData("a/\u0000/b.pdf", false)]
    public void Key_Shape_Validation(string key, bool expected)
    {
        Assert.Equal(expected, DevLocalStorageDeliveryEndpoint.IsWellFormedKey(key));
    }

    // ── signature integrity ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expired_Tampered_Missing_Or_Foreign_Signatures_Are_Rejected()
    {
        var companyId = Guid.NewGuid();
        var (candidateId, documentId, key) = await SeedCvAsync(companyId, CandidateDocumentScanStatus.Clean);
        WriteFile(LocalStorageBuckets.CandidateDocuments, key);
        using var recruiter = await ClientAs(RecruiterUser, companyId);
        var signedUrl = await GetSignedCvUrlAsync(recruiter, companyId, candidateId, documentId);
        var (exp, sig) = ReadSignature(signedUrl);
        var path = signedUrl.AbsolutePath;
        var expValue = long.Parse(exp);

        var tamperedSig = (sig[0] == 'A' ? "B" : "A") + sig[1..];
        var foreign = new LocalStorageUrlSigner(TimeProvider.System)
            .CreateSignedUrl("http://localhost", LocalStorageBuckets.CandidateDocuments, key);
        var (foreignExp, foreignSig) = ReadSignature(foreign);

        var attempts = new[]
        {
            $"{path}?exp={expValue - 3600}&sig={sig}",          // expired (and re-dated) expiry
            $"{path}?exp={expValue + 60}&sig={sig}",            // extended expiry
            $"{path}?exp={exp}&sig={tamperedSig}",              // tampered signature
            $"{path}?exp={exp}",                                // missing signature
            $"{path}?sig={sig}",                                // missing expiry
            $"{path}?exp={exp}&sig={sig}&sig={sig}",            // duplicated parameters
            $"{path}?exp={foreignExp}&sig={foreignSig}",        // signed with another key
        };

        using var anonymous = AnonymousClient();
        foreach (var attempt in attempts)
        {
            var response = await anonymous.GetAsync(attempt);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Expected 404 for '{attempt}', got {(int)response.StatusCode}.");
        }

        // Control: the untampered URL still works.
        var ok = await anonymous.GetAsync(signedUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ── other buckets ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Profile_Photo_Is_Only_Served_Once_Its_Record_Is_Clean()
    {
        var companyId = Guid.NewGuid();
        var key = $"{companyId}/{Guid.NewGuid()}/{Guid.NewGuid():N}.png";
        var bytes = WriteFile(LocalStorageBuckets.ProfilePhotos, key);
        var photoId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            db.PendingProfilePhotos.Add(PendingProfilePhoto.Create(
                photoId, companyId, Guid.NewGuid(), "me.png", bytes.Length, "image/png", key, Guid.NewGuid(), Now));
            await db.SaveChangesAsync();
        }

        var signed = Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.ProfilePhotos, key);
        using var anonymous = AnonymousClient();

        var whilePending = await anonymous.GetAsync(signed.PathAndQuery);
        Assert.Equal(HttpStatusCode.NotFound, whilePending.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var photo = await db.PendingProfilePhotos.SingleAsync(p => p.Id == photoId);
            photo.MarkScanning(DateTimeOffset.UtcNow);
            photo.MarkScanClean(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var onceClean = await anonymous.GetAsync(signed.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, onceClean.StatusCode);
        Assert.Equal(bytes, await onceClean.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Documents_Bucket_Orphaned_File_Is_Denied()
    {
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid():N}.pdf";
        WriteFile(LocalStorageBuckets.Documents, key);
        var signed = Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.Documents, key);
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(signed.PathAndQuery);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Support_Attachments_Bucket_Fails_Closed_Without_A_Resolver()
    {
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid():N}.pdf";
        WriteFile(LocalStorageBuckets.SupportAttachments, key);
        var signed = Signer.CreateSignedUrl("http://localhost", LocalStorageBuckets.SupportAttachments, key);
        using var anonymous = AnonymousClient();

        var response = await anonymous.GetAsync(signed.PathAndQuery);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
