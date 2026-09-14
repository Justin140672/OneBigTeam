using System.Net;
using System.Net.Http.Json;
using HR.Admin.Web.Models;
using HR.Admin.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Admin.Web.Tests;

// Ticket 16: service-level (non-bUnit) coverage of SupportRequestAdminService's version-handling
// logic for the Admin Portal's Support Request status editor. Mirrors the "real DI-registered
// named HttpClient with a fake primary handler" harness already used by HrApiHttpClientFactoryTests
// rather than mocking HttpClient directly.
public class SupportRequestAdminServiceTests
{
    // Responds based on the requested path/method so a single handler can serve every scenario.
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend { get; set; }
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (OnSend is not null)
                return await OnSend(request);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static (SupportRequestAdminService Service, ScriptedHandler Handler) BuildService()
    {
        var handler = new ScriptedHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<CircuitSessionState>();
        services.AddScoped<HrApiHttpClientFactory>();
        services.AddScoped<SupportRequestAdminService>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<SupportRequestAdminService>(), handler);
    }

    [Fact]
    public async Task GetSupportRequestAsync_Returns_Detail_With_Loaded_Version()
    {
        var (service, handler) = BuildService();
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();

        handler.OnSend = async request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains($"api/admin/companies/{companyId}/support/requests/{id}", request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var detail = new SupportRequestDetailModel(
                id, "REF-1", "AskQuestion", "Title", "Description", "Low", "Submitted",
                null, null, null, false, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3, [], []);
            response.Content = JsonContent.Create(detail);
            return response;
        };

        var result = await service.GetSupportRequestAsync(companyId, id);

        Assert.Equal(SupportRequestFetchOutcome.Success, result.Outcome);
        Assert.NotNull(result.Detail);
        Assert.Equal(3, result.Detail!.Version);
    }

    [Fact]
    public async Task GetSupportRequestAsync_Returns_Forbidden_Outcome_For_403()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.NotAnEnabledPlatformAdministrator, result.Outcome);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task GetSupportRequestAsync_Returns_Unauthenticated_Outcome_For_401()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.Unauthenticated, result.Outcome);
    }

    [Fact]
    public async Task GetSupportRequestAsync_Returns_NotFound_Outcome_For_404()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateStatusAsync_Submits_The_Supplied_ExpectedVersion()
    {
        var (service, handler) = BuildService();
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        UpdateSupportRequestStatusRequest? captured = null;

        handler.OnSend = async request =>
        {
            captured = await request.Content!.ReadFromJsonAsync<UpdateSupportRequestStatusRequest>();
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(
                new UpdateSupportRequestStatusResponse(id, "UnderReview", DateTimeOffset.UtcNow, 4));
            return response;
        };

        var result = await service.UpdateStatusAsync(companyId, id, "UnderReview", expectedVersion: 3);

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.ExpectedVersion);
        Assert.Equal(SupportRequestStatusUpdateOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task UpdateStatusAsync_On_Success_Returns_The_Servers_New_Version_For_The_Caller_To_Adopt()
    {
        var (service, handler) = BuildService();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(
                new UpdateSupportRequestStatusResponse(Guid.NewGuid(), "UnderReview", DateTimeOffset.UtcNow, 4));
            return Task.FromResult(response);
        };

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "UnderReview", expectedVersion: 3);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Success, result.Outcome);
        Assert.NotNull(result.Response);
        Assert.Equal(4, result.Response!.Version);
    }

    [Fact]
    public async Task UpdateStatusAsync_On_409_Returns_Conflict_Outcome_With_No_Response_Body()
    {
        var (service, handler) = BuildService();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict);
            response.Content = JsonContent.Create(new
            {
                error = "This support request was changed by someone else since you opened it.",
                code = "concurrency",
            });
            return Task.FromResult(response);
        };

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", expectedVersion: 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Conflict, result.Outcome);
        Assert.Null(result.Response);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task UpdateStatusAsync_On_Other_Failure_Returns_Failed_Outcome_Distinct_From_Conflict()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", expectedVersion: 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Failed, result.Outcome);
        Assert.NotEqual(SupportRequestStatusUpdateOutcome.Conflict, result.Outcome);
    }
}
