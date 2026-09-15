using System.Net;
using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.Admin.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Admin.Web.Tests;

// Ticket 19 (fuller audit sweep): verifies FormText.OptionalSearch is actually applied to the
// query string built by HR.Admin.Web's free-text search/filter service methods, not just to
// the shared helper itself. Mirrors the request-capturing harness used by
// SupportRequestAdminServiceTests.
public class FormTextSearchNormalizationTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string CapturedRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (T Service, CapturingHandler Handler) Build<T>(Func<HrApiHttpClientFactory, T> factory)
    {
        var handler = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var clientFactory = new HrApiHttpClientFactory(provider.GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
        return (factory(clientFactory), handler);
    }

    [Fact]
    public async Task CustomerListService_Trims_Search_Term_In_Query_String()
    {
        var (service, handler) = Build(f => new CustomerListService(f));

        await service.GetCustomersOrNullAsync(search: "  acme  ");

        Assert.Contains("search=acme", handler.CapturedRequestUri);
    }

    [Fact]
    public async Task CustomerListService_Whitespace_Only_Search_Is_Omitted_From_Query_String()
    {
        var (service, handler) = Build(f => new CustomerListService(f));

        await service.GetCustomersOrNullAsync(search: "   ");

        Assert.DoesNotContain("search=", handler.CapturedRequestUri);
        Assert.Equal("http://localhost/api/companies/admin/customers", handler.CapturedRequestUri);
    }

    [Fact]
    public async Task FailedPaymentsService_Trims_Search_Term_In_Query_String()
    {
        var (service, handler) = Build(f => new FailedPaymentsService(f));

        await service.GetFailedPaymentsOrNullAsync(search: "  jane@example.com  ");

        Assert.Contains("search=jane%40example.com", handler.CapturedRequestUri);
    }

    [Fact]
    public async Task AuditLogService_Trims_AdministratorEmail_In_Query_String()
    {
        var (service, handler) = Build(f => new AuditLogService(f));

        await service.GetAuditLogOrNullAsync(administratorEmail: "  admin@example.com  ");

        Assert.Contains("administratorEmail=admin%40example.com", handler.CapturedRequestUri);
    }
}
