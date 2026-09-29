using System.Net;
using System.Net.Http;
using HR.Modules.DataImport.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HR.Modules.DataImport.Tests;

public class SupabaseImportFileStorageRestartSafetyTests
{
    private static IOptions<SupabaseImportFileStorageOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new SupabaseImportFileStorageOptions
        {
            SupabaseUrl = "https://example.supabase.co",
            ServiceRoleKey = "service-role-key",
            BucketName = "import-files",
            SignedUrlExpirySeconds = 3600,
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public byte[] StoredBytes { get; set; } = [];
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));

            if (request.RequestUri!.AbsolutePath.Contains("/sign/"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"signedURL\":\"/storage/v1/object/sign/import-files/signed-token\"}",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(StoredBytes),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Upload_Then_Read_And_Delete_From_A_Fresh_Instance_Succeeds()
    {
        var bytes = new byte[] { 10, 20, 30, 40, 50 };

        var handlerA = new RecordingHandler { StoredBytes = bytes };
        var serviceA = new SupabaseImportFileStorageService(new HttpClient(handlerA), Options());

        using var content = new MemoryStream(bytes);
        var storageKey = serviceA.GenerateStorageKey("companies/c1/imports/session1", "employees.csv");
        await serviceA.UploadAsync(content, storageKey, "text/csv", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(storageKey));
        Assert.DoesNotContain(Path.GetTempPath().Replace('\\', '/'), storageKey, StringComparison.OrdinalIgnoreCase);

        var handlerB = new RecordingHandler { StoredBytes = bytes };
        var serviceB = new SupabaseImportFileStorageService(new HttpClient(handlerB), Options());

        await using (var stream = await serviceB.OpenReadAsync(storageKey, CancellationToken.None))
        {
            using var reader = new MemoryStream();
            await stream.CopyToAsync(reader);
            Assert.Equal(bytes, reader.ToArray());
        }

        var downloadUrl = await serviceB.GetDownloadUrlAsync(storageKey, CancellationToken.None);
        Assert.NotNull(downloadUrl);

        await serviceB.DeleteAsync(storageKey, CancellationToken.None);
        Assert.Contains(handlerB.Requests, r => r.Method == HttpMethod.Delete);
    }
}
