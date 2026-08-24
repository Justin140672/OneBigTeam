using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.DeleteLeaveRequestDraft;
using HR.Modules.Leave.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class DeleteLeaveRequestDraftHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static DeleteLeaveRequestDraftRequest DeleteRequest(Guid companyId, Guid employeeId, Guid leaveRequestId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveRequestId = leaveRequestId
    };

    private static LeaveRequest CreateDraft(Guid companyId, Guid employeeId, DateTimeOffset now) =>
        LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), null,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

    [Fact]
    public async Task HandleAsync_Deletes_Draft_Row()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var draft = CreateDraft(companyId, employeeId, now);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        var handler = new DeleteLeaveRequestDraftHandler(context);
        var result = await handler.HandleAsync(DeleteRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(draft.Id, result.Value!.Id);
        Assert.Empty(context.LeaveRequests);
    }

    [Theory]
    [InlineData((int)LeaveRequestStatus.Pending)]
    [InlineData((int)LeaveRequestStatus.Approved)]
    [InlineData((int)LeaveRequestStatus.Rejected)]
    [InlineData((int)LeaveRequestStatus.Cancelled)]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Is_Not_Draft(int statusValue)
    {
        var status = (LeaveRequestStatus)statusValue;
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var draft = CreateDraft(companyId, employeeId, now);

        switch (status)
        {
            case LeaveRequestStatus.Pending:
                draft.MarkSubmittedPending(now);
                break;
            case LeaveRequestStatus.Approved:
                draft.Approve(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Rejected:
                draft.MarkSubmittedPending(now);
                draft.Reject(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Cancelled:
                draft.MarkSubmittedPending(now);
                draft.Cancel(now);
                break;
        }

        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        var handler = new DeleteLeaveRequestDraftHandler(context);
        var result = await handler.HandleAsync(DeleteRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Single(context.LeaveRequests);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeleteLeaveRequestDraftHandler(context);

        var result = await handler.HandleAsync(
            DeleteRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Draft_Belongs_To_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var draft = CreateDraft(companyId, Guid.NewGuid(), now);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        var handler = new DeleteLeaveRequestDraftHandler(context);
        var result = await handler.HandleAsync(DeleteRequest(companyId, Guid.NewGuid(), draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}

public class DeleteLeaveRequestDraftValidatorTests
{
    private readonly DeleteLeaveRequestDraftValidator _validator = new();

    private static DeleteLeaveRequestDraftRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty }).IsValid);
    }
}
