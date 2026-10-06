using System.Net;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Storage;
using HR.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Tests.Storage;

public sealed class SupabaseProfilePhotoStorageServiceTests
{
    private const string BaseUrl = "https://project.supabase.example";
    private const string ServiceKey = "service-role-secret";

    private static (SupabaseProfilePhotoStorageService Sut, FakeHttpMessageHandler Handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new SupabaseProfilePhotoStorageOptions
        {
            SupabaseUrl = BaseUrl,
            ServiceRoleKey = ServiceKey,
            BucketName = "profile-photos",
        });

        return (new SupabaseProfilePhotoStorageService(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task OpenReadAsync_Reads_With_Service_Credentials_And_Never_Signs_A_Url()
    {
        var (sut, handler) = Create();
        handler.ResponseBodyToReturn = "image-bytes";

        await using var stream = await sut.OpenReadAsync("quarantine/c/e/abc/photo.png", CancellationToken.None);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Equal("image-bytes", content);
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}/storage/v1/object/authenticated/profile-photos/quarantine/c/e/abc/photo.png", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ServiceKey, request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("/object/sign/", request.RequestUri.ToString());
    }

    [Fact]
    public async Task OpenReadAsync_Throws_When_Object_Is_Missing()
    {
        var (sut, handler) = Create();
        handler.StatusCodeToReturn = HttpStatusCode.NotFound;

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.OpenReadAsync("quarantine/missing.png", CancellationToken.None));
    }

    [Fact]
    public async Task PromoteToCleanAsync_Copies_Quarantine_Object_To_A_New_Clean_Key_And_Keeps_The_Source()
    {
        var (sut, handler) = Create();
        const string source = "quarantine/company/employee/abc/photo.png";

        var cleanKey = await sut.PromoteToCleanAsync(source, CancellationToken.None);

        Assert.StartsWith("clean/company/employee/", cleanKey);
        Assert.EndsWith("/photo.png", cleanKey);
        Assert.NotEqual(source, cleanKey);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{BaseUrl}/storage/v1/object/copy", request.RequestUri!.ToString());
        Assert.Contains(source, body);
        Assert.Contains(cleanKey, body);
        Assert.DoesNotContain(handler.Requests, r => r.Request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task PromoteToCleanAsync_Returns_Non_Quarantine_Keys_Unchanged_Without_Calling_Storage()
    {
        var (sut, handler) = Create();

        var result = await sut.PromoteToCleanAsync("legacy/company/employee/photo.png", CancellationToken.None);

        Assert.Equal("legacy/company/employee/photo.png", result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PromoteToCleanAsync_Propagates_Storage_Failure()
    {
        var (sut, handler) = Create();
        handler.StatusCodeToReturn = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.PromoteToCleanAsync("quarantine/c/e/abc/photo.png", CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_Stores_Under_The_Supplied_Quarantine_Folder()
    {
        var (sut, handler) = Create();
        using var content = new MemoryStream([1, 2, 3]);

        var key = await sut.UploadAsync(
            content, "photo.png", "image/png", ProfilePhotoStorageKeys.QuarantineFolder("company/employee"), CancellationToken.None);

        Assert.StartsWith("quarantine/company/employee/", key);
        Assert.Contains("/storage/v1/object/profile-photos/quarantine/company/employee/", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetDownloadUrlAsync_Signs_The_Exact_Key_It_Is_Given()
    {
        var (sut, handler) = Create();
        handler.ResponseBodyToReturn = "{\"signedURL\":\"/object/sign/profile-photos/clean/c/e/x/photo.png?token=t\"}";

        var url = await sut.GetDownloadUrlAsync("clean/c/e/x/photo.png", CancellationToken.None);

        Assert.StartsWith(BaseUrl, url.ToString());
        Assert.Equal($"{BaseUrl}/storage/v1/object/sign/profile-photos/clean/c/e/x/photo.png", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("quarantine/a/b/c/photo.png", true)]
    [InlineData("clean/a/b/c/photo.png", false)]
    [InlineData("a/b/photo.png", false)]
    public void ProfilePhotoStorageKeys_Recognise_Quarantine(string key, bool expected)
    {
        Assert.Equal(expected, ProfilePhotoStorageKeys.IsQuarantine(key));
    }

    [Fact]
    public void ProfilePhotoStorageKeys_ToCleanKey_Is_Unique_Per_Call()
    {
        const string source = "quarantine/a/b/c/photo.png";

        Assert.NotEqual(ProfilePhotoStorageKeys.ToCleanKey(source), ProfilePhotoStorageKeys.ToCleanKey(source));
    }
}
