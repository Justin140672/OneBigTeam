using System.Net;
using System.Net.Http.Json;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class DocumentServiceTests
{
    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    // ── ArchiveSharedCompanyDocumentAsync ────────────────────────────────────────

    [Fact]
    public async Task ArchiveSharedCompanyDocumentAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new DocumentService(factory);

        var error = await service.ArchiveSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.Null(error);
    }

    [Fact]
    public async Task ArchiveSharedCompanyDocumentAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "Reason is required." }));
        var service = new DocumentService(factory);

        var error = await service.ArchiveSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.Equal("Reason is required.", error);
    }

    [Fact]
    public async Task ArchiveSharedCompanyDocumentAsync_Returns_ConflictMessage_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This document has already been archived." }));
        var service = new DocumentService(factory);

        var error = await service.ArchiveSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.Equal("This document has already been archived.", error);
    }

    [Fact]
    public async Task ArchiveSharedCompanyDocumentAsync_Returns_Fallback_Message_When_Api_Returns_Unmapped_Error()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, new { }));
        var service = new DocumentService(factory);

        var error = await service.ArchiveSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.NotNull(error);
    }

    // ── PublishSharedCompanyDocumentAsync ────────────────────────────────────────

    [Fact]
    public async Task PublishSharedCompanyDocumentAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new DocumentService(factory);

        var error = await service.PublishSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(error);
    }

    [Fact]
    public async Task PublishSharedCompanyDocumentAsync_Returns_ValidationMessage_When_Api_Returns_Validation_Envelope()
    {
        var errors = new Dictionary<string, string[]> { ["CategoryId"] = ["A category is required before publishing."] };
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, new { errors }));
        var service = new DocumentService(factory);

        var error = await service.PublishSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("A category is required before publishing.", error);
    }

    [Fact]
    public async Task PublishSharedCompanyDocumentAsync_Returns_ConflictMessage_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This document has already been published." }));
        var service = new DocumentService(factory);

        var error = await service.PublishSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("This document has already been published.", error);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object? payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (payload is not null)
                response.Content = JsonContent.Create(payload);
            return Task.FromResult(response);
        }
    }
}
