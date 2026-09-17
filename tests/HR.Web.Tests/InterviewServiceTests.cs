using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class InterviewServiceTests
{
    // ── GetInterviewsTodayCountAsync (representative read) ───────────────────────

    [Fact]
    public async Task GetInterviewsTodayCountAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new GetInterviewsTodayCountResponse(3)));
        var service = new InterviewService(factory);

        var result = await service.GetInterviewsTodayCountAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
    }

    [Fact]
    public async Task GetInterviewsTodayCountAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new InterviewService(factory);

        var result = await service.GetInterviewsTodayCountAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInterviewsTodayCountAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new InterviewService(factory);

        var result = await service.GetInterviewsTodayCountAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInterviewsTodayCountAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new GetInterviewsTodayCountResponse(1)));
        var service = new InterviewService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetInterviewsTodayCountAsync(Guid.NewGuid(), cts.Token));
    }

    // ── ScheduleInterviewAsync (write) ───────────────────────────────────────────

    [Fact]
    public async Task ScheduleInterviewAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new ScheduleInterviewResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 30, null, "Pending", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new InterviewService(factory);

        var (result, error) = await service.ScheduleInterviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new ScheduleInterviewRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 30, null));

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task ScheduleInterviewAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "Scheduled time must be in the future." }));
        var service = new InterviewService(factory);

        var (result, error) = await service.ScheduleInterviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new ScheduleInterviewRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 30, null));

        Assert.Null(result);
        Assert.Equal("Scheduled time must be in the future.", error);
    }

    [Fact]
    public async Task ScheduleInterviewAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new InterviewService(factory);

        var (result, error) = await service.ScheduleInterviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new ScheduleInterviewRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 30, null));

        Assert.Null(result);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }

    [Fact]
    public async Task ScheduleInterviewAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler(HttpStatusCode.Created));
        var service = new InterviewService(factory);

        var (result, error) = await service.ScheduleInterviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new ScheduleInterviewRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 30, null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ── RecordInterviewOutcomeAsync (write) ──────────────────────────────────────

    [Fact]
    public async Task RecordInterviewOutcomeAsync_Returns_Failure_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This interview's outcome was already recorded." }));
        var service = new InterviewService(factory);

        var (result, error) = await service.RecordInterviewOutcomeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Passed", null);

        Assert.Null(result);
        Assert.Equal("This interview's outcome was already recorded.", error);
    }
}
