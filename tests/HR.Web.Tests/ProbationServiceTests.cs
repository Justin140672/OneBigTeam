using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

// Ticket 17: ProbationService.UpdateProbationRecordAsync — administrative-correction save with
// optimistic concurrency. Mirrors EmployeeServiceTests' harness (real DI-registered named
// HttpClient over a fake primary handler) and EmployeeService.UpdateEmploymentDetailsAsync's
// error-shape handling ({ error, code } vs FastEndpoints' 422 { statusCode, message, errors }).
public class ProbationServiceTests
{
    private static (ProbationService Service, HttpMessageHandlerStub Handler) BuildService()
    {
        var handler = new HttpMessageHandlerStub();
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = new HrApiHttpClientFactory(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
        return (new ProbationService(factory), handler);
    }

    private static UpdateProbationRecordApiRequest SampleRequest(int? expectedVersion = 3) => new(
        CompanyId: Guid.NewGuid(),
        ProbationRecordId: Guid.NewGuid(),
        ManagerEmployeeId: Guid.NewGuid(),
        ExpectedEndDate: DateOnly.FromDateTime(DateTime.Today).AddMonths(3),
        Notes: "Some notes",
        ExpectedVersion: expectedVersion);

    private sealed class HttpMessageHandlerStub : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => OnSend?.Invoke(request) ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_On_Success_Returns_Ok_With_NewVersion()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UpdateProbationRecordApiResponse(
                    request.ProbationRecordId, request.CompanyId, Guid.NewGuid(), request.ManagerEmployeeId,
                    DateOnly.FromDateTime(DateTime.Today), request.ExpectedEndDate, "Active",
                    request.Notes, null, null, null, null, DateTimeOffset.UtcNow, 4)),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.True(result.Success);
        Assert.Equal(4, result.NewVersion);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_On_409_Returns_Fail_With_IsConcurrencyConflict_True()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new
                {
                    error = "This probation record was changed by someone else since you opened it.",
                    code = "concurrency",
                }),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.NewVersion);
    }

    // Ticket 18: a business-rule 409 (terminal-status rejection, code == "conflict") must NOT be
    // treated as a stale-write concurrency conflict — the UI relies on this to avoid showing the
    // "reload latest values" banner for an ordinary rule violation.
    [Fact]
    public async Task UpdateProbationRecordAsync_On_409_With_ConflictCode_Returns_Fail_With_IsConcurrencyConflict_False()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new
                {
                    error = "Cannot edit a probation record that has already reached the terminal status 'Passed'.",
                    code = "conflict",
                }),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.Equal(
            "Cannot edit a probation record that has already reached the terminal status 'Passed'.",
            result.ErrorMessage);
        Assert.Null(result.NewVersion);
    }

    // A malformed/empty/unrecognized 409 body must not be mistaken for either known code — treated
    // as an ordinary failure with a generic fallback message.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"error\":\"Something went wrong.\",\"code\":\"something-else\"}")]
    public async Task UpdateProbationRecordAsync_On_409_With_Malformed_Or_Unknown_Body_Returns_Ordinary_Failure(string body)
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.NewVersion);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_On_Other_Failure_Returns_Fail_With_IsConcurrencyConflict_False()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_On_422_Validation_Shape_Surfaces_Field_Error_Message()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)422)
            {
                Content = JsonContent.Create(new
                {
                    statusCode = 422,
                    message = "One or more validation errors occurred.",
                    errors = new Dictionary<string, string[]>
                    {
                        ["ExpectedEndDate"] = ["Expected end date must be after the start date."],
                    },
                }),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.Contains("Expected end date must be after the start date.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_On_BusinessRule_ErrorEnvelope_Surfaces_Message()
    {
        var (service, handler) = BuildService();
        var request = SampleRequest();

        handler.OnSend = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "Probation record not found." }),
            };
            return Task.FromResult(response);
        };

        var result = await service.UpdateProbationRecordAsync(request.CompanyId, request);

        Assert.False(result.Success);
        Assert.Equal("Probation record not found.", result.ErrorMessage);
    }
}
