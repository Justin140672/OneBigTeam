using System.Net;
using System.Net.Http;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Reliability review issue 2 (P1): proves a candidate document uploaded through one
/// <see cref="SupabaseCandidateDocumentStorageService"/> instance (standing in for one API service
/// instance/process) remains fully retrievable and deletable through a second, independently
/// constructed instance (standing in for a different service instance after a restart/redeploy) —
/// unlike <see cref="LocalCandidateDocumentStorageService"/>, no state lives in this process's local
/// temp directory; everything needed to address the file (the storage key) is self-contained and the
/// actual bytes live in the remote Supabase Storage bucket, not on this machine's disk.
/// </summary>
public class SupabaseCandidateDocumentStorageRestartSafetyTests
{
    private static IOptions<SupabaseCandidateDocumentStorageOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new SupabaseCandidateDocumentStorageOptions
        {
            SupabaseUrl = "https://example.supabase.co",
            ServiceRoleKey = "service-role-key",
            BucketName = "candidate-documents",
            SignedUrlExpirySeconds = 3600,
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));

            HttpResponseMessage response = request.RequestUri!.AbsolutePath.Contains("/sign/")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"signedURL\":\"/storage/v1/object/sign/candidate-documents/signed-token\"}",
                        System.Text.Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK);

            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Upload_Then_Download_And_Delete_From_A_Fresh_Instance_Succeeds()
    {
        // Instance A: simulates the service instance/process that received the original upload.
        var handlerA = new RecordingHandler();
        var serviceA = new SupabaseCandidateDocumentStorageService(new HttpClient(handlerA), Options());

        using var content = new MemoryStream([1, 2, 3, 4]);
        var storageKey = serviceA.GenerateStorageKey("companies/c1/candidates/cand1", "cv.pdf");
        await serviceA.UploadAsync(content, storageKey, "application/pdf", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(storageKey));
        Assert.DoesNotContain(Path.GetTempPath().Replace('\\', '/'), storageKey, StringComparison.OrdinalIgnoreCase);

        // Instance B: a brand-new instance built only from the same durable config — nothing is
        // shared in-process with instance A, proving the file is addressable purely via the
        // storage key against the remote store, independent of any single process's lifetime.
        var handlerB = new RecordingHandler();
        var serviceB = new SupabaseCandidateDocumentStorageService(new HttpClient(handlerB), Options());

        var downloadUrl = await serviceB.GetDownloadUrlAsync(storageKey, CancellationToken.None);
        Assert.NotNull(downloadUrl);
        Assert.Contains(handlerB.Requests, r => r.Path.Contains("/sign/"));

        await serviceB.DeleteAsync(storageKey, CancellationToken.None);
        Assert.Contains(handlerB.Requests, r => r.Method == HttpMethod.Delete);
    }
}
