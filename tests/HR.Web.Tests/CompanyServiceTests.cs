using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class CompanyServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    // ── GetCompanySettingsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCompanySettingsAsync_Returns_Settings_Including_Contact_Validation_Regexes()
    {
        var response = new GetCompanySettingsResponse(
            Guid.NewGuid(), "UTC", "en-GB",
            "^[A-Za-z]{1,2}\\d[A-Za-z\\d]?\\s?\\d[A-Za-z]{2}$", "^0\\d{9,10}$", "^07\\d{9}$",
            DateTime.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompanyService(factory);

        var result = await service.GetCompanySettingsAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal("UTC", result!.TimeZone);
        Assert.False(string.IsNullOrEmpty(result.PostcodeRegex));
        Assert.False(string.IsNullOrEmpty(result.MobileRegex));
    }

    [Fact]
    public async Task GetCompanySettingsAsync_Returns_Null_When_Network_Fails()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompanyService(factory);

        var result = await service.GetCompanySettingsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── GetCompanyAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCompanyAsync_Returns_Company_When_Api_Returns_Ok()
    {
        var response = new GetCompanyResponse(Guid.NewGuid(), "Acme Corporation", true, DateTime.UtcNow, [], null);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompanyService(factory);

        var result = await service.GetCompanyAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal("Acme Corporation", result!.Name);
    }

    [Fact]
    public async Task GetCompanyAsync_Returns_Null_When_Network_Fails()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompanyService(factory);

        var result = await service.GetCompanyAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── UpdateCompanyAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCompanyAsync_Returns_Response_When_Api_Returns_Ok()
    {
        var response = new UpdateCompanyResponse(Guid.NewGuid(), "Acme Corporation", true);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompanyService(factory);

        var (result, error) = await service.UpdateCompanyAsync(Guid.NewGuid(), new UpdateCompanyRequest(Guid.NewGuid(), "Acme Corporation", []));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal("Acme Corporation", result!.Name);
    }

    [Fact]
    public async Task UpdateCompanyAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        // This is the new server-side postcode-regex validation failure path added this session.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'not a postcode' is not a valid postcode." }));
        var service = new CompanyService(factory);

        var (result, error) = await service.UpdateCompanyAsync(Guid.NewGuid(), new UpdateCompanyRequest(Guid.NewGuid(), "Acme Corporation", []));

        Assert.Null(result);
        Assert.Equal("'not a postcode' is not a valid postcode.", error);
    }

    [Fact]
    public async Task UpdateCompanyAsync_Returns_GenericMessage_When_Error_Body_Has_No_Error_Field()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, new { }));
        var service = new CompanyService(factory);

        var (result, error) = await service.UpdateCompanyAsync(Guid.NewGuid(), new UpdateCompanyRequest(Guid.NewGuid(), "Acme Corporation", []));

        Assert.Null(result);
        Assert.Equal("Failed to save company profile.", error);
    }

    // ── UpdateCompanySettingsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateCompanySettingsAsync_Returns_Response_When_Api_Returns_Ok()
    {
        var response = new UpdateCompanySettingsResponse(
            Guid.NewGuid(), "UTC", "en-GB", DateTime.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompanyService(factory);

        var request = new UpdateCompanySettingsRequest(Guid.NewGuid(), "UTC", "en-GB");

        var result = await service.UpdateCompanySettingsAsync(Guid.NewGuid(), request);

        Assert.NotNull(result);
        Assert.Equal("UTC", result!.TimeZone);
    }

    [Fact]
    public async Task UpdateCompanySettingsAsync_Returns_Null_When_Api_Returns_Error()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { }));
        var service = new CompanyService(factory);

        var request = new UpdateCompanySettingsRequest(Guid.NewGuid(), "UTC", "en-GB");

        var result = await service.UpdateCompanySettingsAsync(Guid.NewGuid(), request);

        Assert.Null(result);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
