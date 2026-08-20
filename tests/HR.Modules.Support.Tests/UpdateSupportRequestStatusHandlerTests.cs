using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.UpdateSupportRequestStatus;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Tests;

public class UpdateSupportRequestStatusHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SupportRequest CreateRequest(Guid companyId, SupportRequestStatus initialStatus = SupportRequestStatus.Submitted)
    {
        var entity = SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", null, null, null, false, null, null, SeedNow);

        if (initialStatus != SupportRequestStatus.Submitted)
            entity.ChangeStatus(initialStatus, SeedNow);

        return entity;
    }

    [Theory]
    [InlineData((int)SupportRequestStatus.Submitted, (int)SupportRequestStatus.UnderReview)]
    [InlineData((int)SupportRequestStatus.UnderReview, (int)SupportRequestStatus.Planned)]
    [InlineData((int)SupportRequestStatus.Planned, (int)SupportRequestStatus.WaitingForCustomer)]
    [InlineData((int)SupportRequestStatus.WaitingForCustomer, (int)SupportRequestStatus.Resolved)]
    [InlineData((int)SupportRequestStatus.Resolved, (int)SupportRequestStatus.Closed)]
    [InlineData((int)SupportRequestStatus.Closed, (int)SupportRequestStatus.UnderReview)] // reopening via an active state, not directly to Submitted, is allowed
    [InlineData((int)SupportRequestStatus.Submitted, (int)SupportRequestStatus.Submitted)] // no-op transition allowed
    public async Task HandleAsync_Allows_Transition(int fromValue, int toValue)
    {
        var from = (SupportRequestStatus)fromValue;
        var to   = (SupportRequestStatus)toValue;
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, from);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), new FakeHrAdministratorDirectory(), new FakeNotificationWriter());
        var result = await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = companyId, Id = request.Id, Status = to },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(to.ToString(), result.Value!.Status);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(to, saved.Status);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Reopening_Closed_Request_Directly_To_Submitted()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, SupportRequestStatus.Closed);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), new FakeHrAdministratorDirectory(), new FakeNotificationWriter());
        var result = await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = companyId, Id = request.Id, Status = SupportRequestStatus.Submitted },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(SupportRequestStatus.Closed, saved.Status); // unchanged
    }

    [Fact]
    public async Task HandleAsync_Notifies_Every_HrAdministrator_When_Status_Actually_Changes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, SupportRequestStatus.Submitted);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var hrAdminDirectory = new FakeHrAdministratorDirectory();
        var hrAdmin1 = Guid.NewGuid();
        var hrAdmin2 = Guid.NewGuid();
        hrAdminDirectory.Seed(companyId, hrAdmin1, hrAdmin2);
        var notificationWriter = new FakeNotificationWriter();

        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), hrAdminDirectory, notificationWriter);

        var result = await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = companyId, Id = request.Id, Status = SupportRequestStatus.UnderReview },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, notificationWriter.WrittenNotifications.Count);
        Assert.Contains(notificationWriter.WrittenNotifications, n => n.EmployeeId == hrAdmin1 && n.Type == NotificationType.SupportRequestStatusChanged);
        Assert.Contains(notificationWriter.WrittenNotifications, n => n.EmployeeId == hrAdmin2 && n.Type == NotificationType.SupportRequestStatusChanged);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Notify_On_A_No_Op_Status_Transition()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, SupportRequestStatus.Submitted);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var hrAdminDirectory = new FakeHrAdministratorDirectory();
        hrAdminDirectory.Seed(companyId, Guid.NewGuid());
        var notificationWriter = new FakeNotificationWriter();

        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), hrAdminDirectory, notificationWriter);

        await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = companyId, Id = request.Id, Status = SupportRequestStatus.Submitted },
            CancellationToken.None);

        Assert.Empty(notificationWriter.WrittenNotifications);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), new FakeHrAdministratorDirectory(), new FakeNotificationWriter());

        var result = await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), Status = SupportRequestStatus.Resolved },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var request = CreateRequest(Guid.NewGuid());
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new UpdateSupportRequestStatusHandler(db, new FakeClock(FixedUtcNow), new FakeHrAdministratorDirectory(), new FakeNotificationWriter());
        var result = await handler.HandleAsync(
            new UpdateSupportRequestStatusRequest { CompanyId = Guid.NewGuid(), Id = request.Id, Status = SupportRequestStatus.Resolved },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
