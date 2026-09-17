using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

/// <summary>
/// Shared fakes/helpers for the HR.SharedKernel.Http-based service tests, following the pattern
/// established in EmployeeServiceTests/DocumentServiceTests. Centralised here to avoid duplicating
/// the same HttpMessageHandler fakes across every migrated service's test file.
/// </summary>
internal static class ApiTestSupport
{
    public static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    /// <summary>Returns a fixed status code with an optional JSON payload (serialized with the given options).</summary>
    public sealed class JsonResponseHandler(HttpStatusCode statusCode, object? payload, System.Text.Json.JsonSerializerOptions? jsonOptions = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (payload is not null)
                response.Content = jsonOptions is not null ? JsonContent.Create(payload, options: jsonOptions) : JsonContent.Create(payload);
            return Task.FromResult(response);
        }
    }

    /// <summary>Returns a 2xx status with an unparseable body, to verify malformed-JSON handling is controlled, not throwing.</summary>
    public sealed class MalformedJsonHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{ this is not valid json", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>Simulates a network-level failure (DNS/connection failure) rather than an HTTP response.</summary>
    public sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
