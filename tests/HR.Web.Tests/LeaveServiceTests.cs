using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class LeaveServiceTests
{
    // ── CancelLeaveRequestAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelLeaveRequestAsync_Returns_True_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new LeaveService(factory);

        var result = await service.CancelLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task CancelLeaveRequestAsync_Returns_False_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new LeaveService(factory);

        var result = await service.CancelLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task CancelLeaveRequestAsync_Returns_False_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new LeaveService(factory);

        var result = await service.CancelLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task CancelLeaveRequestAsync_Returns_False_When_Api_Returns_Conflict_For_Already_Cancelled_Request()
    {
        // Guards against cancelling an already-cancelled/approved-past request.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This leave request cannot be cancelled." }));
        var service = new LeaveService(factory);

        var result = await service.CancelLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task CancelLeaveRequestAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new LeaveService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CancelLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }

    // ── SubmitLeaveRequestAsync (write) ──────────────────────────────────────────

    [Fact]
    public async Task SubmitLeaveRequestAsync_Returns_Response_When_Api_Returns_Ok()
    {
        var response = new SubmitLeaveResponse(Guid.NewGuid(), "Pending", 3m);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeaveService(factory);

        var request = new SubmitLeaveRequestModel(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, null);
        var (result, error) = await service.SubmitLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), request);

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task SubmitLeaveRequestAsync_Returns_ValidationMessage_When_Api_Returns_UnprocessableEntity()
    {
        var errors = new Dictionary<string, string[]> { ["EndDate"] = ["End date must be on or after the start date."] };
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, new { errors }));
        var service = new LeaveService(factory);

        var request = new SubmitLeaveRequestModel(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, null);
        var (result, error) = await service.SubmitLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), request);

        Assert.Null(result);
        Assert.Equal("End date must be on or after the start date.", error);
    }

    [Fact]
    public async Task SubmitLeaveRequestAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new LeaveService(factory);

        var request = new SubmitLeaveRequestModel(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, null);
        var (result, error) = await service.SubmitLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), request);

        Assert.Null(result);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }

    [Fact]
    public async Task SubmitLeaveRequestAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new LeaveService(factory);

        var request = new SubmitLeaveRequestModel(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, DateOnly.FromDateTime(DateTime.Today), LeaveDayPart.FullDay, null);
        var (result, error) = await service.SubmitLeaveRequestAsync(Guid.NewGuid(), Guid.NewGuid(), request);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ── GetEmployeeLeaveBalanceAsync (representative read) ───────────────────────

    [Fact]
    public async Task GetEmployeeLeaveBalanceAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new LeaveBalanceResponse(Guid.NewGuid(), DateTime.UtcNow.Year, []);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeaveService(factory);

        var result = await service.GetEmployeeLeaveBalanceAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEmployeeLeaveBalanceAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new LeaveService(factory);

        var result = await service.GetEmployeeLeaveBalanceAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }
}
