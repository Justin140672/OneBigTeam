using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class RecruitmentKanbanServiceTests
{
    // ── GetKanbanAsync (representative read) ─────────────────────────────────────

    [Fact]
    public async Task GetKanbanAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new GetRecruitmentKanbanResponse(Guid.NewGuid(), "Software Engineer", []);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new RecruitmentKanbanService(factory);

        var result = await service.GetKanbanAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetKanbanAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new RecruitmentKanbanService(factory);

        var result = await service.GetKanbanAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKanbanAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new RecruitmentKanbanService(factory);

        var result = await service.GetKanbanAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetKanbanAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new GetRecruitmentKanbanResponse(Guid.NewGuid(), "x", [])));
        var service = new RecruitmentKanbanService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetKanbanAsync(Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }

    // ── MoveApplicationStageAsync (write) ─────────────────────────────────────────

    [Fact]
    public async Task MoveApplicationStageAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var response = new MoveApplicationStageResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new RecruitmentKanbanService(factory);

        var (result, error) = await service.MoveApplicationStageAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task MoveApplicationStageAsync_Returns_Failure_When_Api_Returns_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This application has already moved to a different stage." }));
        var service = new RecruitmentKanbanService(factory);

        var (result, error) = await service.MoveApplicationStageAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal("This application has already moved to a different stage.", error);
    }

    [Fact]
    public async Task MoveApplicationStageAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new RecruitmentKanbanService(factory);

        var (result, error) = await service.MoveApplicationStageAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal("You do not have permission to perform this action.", error);
    }

    [Fact]
    public async Task MoveApplicationStageAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new RecruitmentKanbanService(factory);

        var (result, error) = await service.MoveApplicationStageAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
        Assert.NotNull(error);
    }
}
