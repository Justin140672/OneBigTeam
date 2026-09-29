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

    // ---- Ticket 3: failures are never represented as empty/not-found data; cancellation propagates ----

    [Fact]
    public async Task ListSupportRequestsAsync_Success_With_Empty_Array_Is_A_Successful_Empty_List()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<SupportRequestListItem>()) });

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.Success, result.Outcome);
        Assert.NotNull(result.Items);
        Assert.Empty(result.Items!);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SupportRequestFetchOutcome.Unauthenticated)]
    [InlineData(HttpStatusCode.Forbidden, SupportRequestFetchOutcome.NotAnEnabledPlatformAdministrator)]
    [InlineData(HttpStatusCode.NotFound, SupportRequestFetchOutcome.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, SupportRequestFetchOutcome.Failed)]
    [InlineData(HttpStatusCode.BadGateway, SupportRequestFetchOutcome.Failed)]
    public async Task ListSupportRequestsAsync_Maps_Each_Failure_Distinctly_And_Returns_No_Items(
        HttpStatusCode status, SupportRequestFetchOutcome expected)
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(status));

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Items);
    }

    [Fact]
    public async Task ListSupportRequestsAsync_Network_Failure_Is_Failed_Not_Empty()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => throw new HttpRequestException("connection refused");

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.Failed, result.Outcome);
        Assert.Null(result.Items);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Fetches_Treat_A_200_With_Malformed_Empty_Or_Null_Body_As_Failed(string body)
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });

        var list = await service.ListSupportRequestsAsync(Guid.NewGuid());
        var detail = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.Failed, list.Outcome);
        Assert.Equal(SupportRequestFetchOutcome.Failed, detail.Outcome);
        Assert.Null(list.Items);
        Assert.Null(detail.Detail);
    }

    [Fact]
    public async Task GetSupportRequestAsync_Server_Error_Is_Failed_Not_NotFound()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SupportRequestFetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Reads_And_Writes_Propagate_Caller_Cancellation()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ListSupportRequestsAsync(Guid.NewGuid(), cancellationToken: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", 1, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UpdateStatusAsync_Failure_Is_Never_Success_And_Message_Is_Safe(HttpStatusCode status)
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(new { error = "System.Exception: SELECT * FROM secrets" }),
        });

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Failed, result.Outcome);
        Assert.Null(result.Response);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.DoesNotContain("secrets", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateStatusAsync_Network_Failure_Is_Failed_With_Retryable_Message()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => throw new HttpRequestException("boom with internal host name db-01");

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("db-01", result.ErrorMessage);
        Assert.Contains("try again", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatusAsync_Validation_Failure_Passes_Through_Api_Message()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { error = "Cannot move a closed request back to Submitted." }),
        });

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Submitted", 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Failed, result.Outcome);
        Assert.Equal("Cannot move a closed request back to Submitted.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateStatusAsync_Malformed_Success_Body_Is_Not_Reported_As_Success()
    {
        var (service, handler) = BuildService();
        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json"),
        });

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Planned", 1);

        Assert.Equal(SupportRequestStatusUpdateOutcome.Failed, result.Outcome);
        Assert.Null(result.Response);
    }
}
