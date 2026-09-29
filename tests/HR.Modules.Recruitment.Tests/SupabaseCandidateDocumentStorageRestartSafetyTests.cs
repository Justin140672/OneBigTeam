using System.Net;
using System.Net.Http;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HR.Modules.Recruitment.Tests;

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
        var handlerA = new RecordingHandler();
        var serviceA = new SupabaseCandidateDocumentStorageService(new HttpClient(handlerA), Options());

        using var content = new MemoryStream([1, 2, 3, 4]);
        var storageKey = serviceA.GenerateStorageKey("companies/c1/candidates/cand1", "cv.pdf");
        await serviceA.UploadAsync(content, storageKey, "application/pdf", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(storageKey));
        Assert.DoesNotContain(Path.GetTempPath().Replace('\\', '/'), storageKey, StringComparison.OrdinalIgnoreCase);

        var handlerB = new RecordingHandler();
        var serviceB = new SupabaseCandidateDocumentStorageService(new HttpClient(handlerB), Options());

        var downloadUrl = await serviceB.GetDownloadUrlAsync(storageKey, CancellationToken.None);
        Assert.NotNull(downloadUrl);
        Assert.Contains(handlerB.Requests, r => r.Path.Contains("/sign/"));

        await serviceB.DeleteAsync(storageKey, CancellationToken.None);
        Assert.Contains(handlerB.Requests, r => r.Method == HttpMethod.Delete);
    }
}
