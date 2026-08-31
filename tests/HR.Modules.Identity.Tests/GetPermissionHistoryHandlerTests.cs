using HR.Modules.Identity.Features.GetPermissionHistory;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: unit tests for <see cref="GetPermissionHistoryHandler"/> — filtering, paging, and
/// event-type inclusion against the platform audit log via <see cref="IAuditHistoryReader"/>.
/// </summary>
public class GetPermissionHistoryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static GetPermissionHistoryHandler BuildHandler(
        FakeAuditHistoryReader reader, Dictionary<Guid, string>? names = null) =>
        new(reader, new FakeEmployeeNameReader(names));

    private static AuditHistoryEntry Entry(
        string eventType,
        DateTimeOffset occurredAt,
        Guid? actorUserId = null,
        Guid? employeeId = null,
        Guid entityId = default,
        string? summary = null,
        string? beforeJson = null,
        string? afterJson = null) =>
        new(occurredAt, eventType, "ApplicationUser", actorUserId, null, summary, beforeJson, afterJson, employeeId, entityId);

    [Fact]
    public async Task HandleAsync_Only_Includes_Permission_Related_Event_Types()
    {
        var now = DateTimeOffset.UtcNow;
        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now),
            Entry("user.role-override-created", now),
            Entry("user.disabled", now),
            Entry("user.enabled", now),
            Entry("user.permission-denied", now),
            Entry("position.role-defaults-changed", now),
            Entry("employee.inherited-roles-recalculated", now),
            Entry("user.role-override-removed", now),
            Entry("user.role-override-expired", now),
            Entry("user.role-change-rejected", now),
            Entry("some.unrelated.event", now),
            Entry("user.invited", now),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        Assert.Equal(10, result.TotalCount);
        Assert.DoesNotContain(result.Items, i => i.EventType is "some.unrelated.event" or "user.invited");
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EmployeeId_Matching_Either_EmployeeId_Or_EntityId()
    {
        var now = DateTimeOffset.UtcNow;
        var targetEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now, employeeId: targetEmployeeId),
            Entry("user.role-override-created", now, entityId: targetEmployeeId),
            Entry("user.roles-changed", now, employeeId: otherEmployeeId),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, EmployeeId = targetEmployeeId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.NotEqual(otherEmployeeId, i.TargetEmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Filter_By_EmployeeId_When_Not_Supplied()
    {
        var now = DateTimeOffset.UtcNow;
        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now, employeeId: Guid.NewGuid()),
            Entry("user.roles-changed", now, employeeId: Guid.NewGuid()),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Orders_Results_Newest_First()
    {
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", older),
            Entry("user.roles-changed", newer),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        Assert.Equal(newer, result.Items[0].OccurredAt);
        Assert.Equal(older, result.Items[1].OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Pages_The_Filtered_Results()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 5)
            .Select(i => Entry("user.roles-changed", now.AddMinutes(i)))
            .ToList();
        var reader = new FakeAuditHistoryReader().WithPlatformEntries(entries);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task HandleAsync_Resolves_PerformedBy_Name_For_Actor_And_Falls_Back_To_System_When_No_Actor()
    {
        var now = DateTimeOffset.UtcNow;
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now, actorUserId: actorId),
            Entry("user.role-override-expired", now, actorUserId: null),
        ]);

        var handler = BuildHandler(reader, new Dictionary<Guid, string> { [actorId] = "Jane Admin" });

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        Assert.Contains(result.Items, i => i.PerformedBy == "Jane Admin");
        Assert.Contains(result.Items, i => i.PerformedBy == "System");
    }

    [Fact]
    public async Task HandleAsync_Maps_Before_And_After_Json_As_PreviousAccess_And_NewAccess()
    {
        var now = DateTimeOffset.UtcNow;
        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now,
                beforeJson: "{\"roles\":[\"Employee\"]}", afterJson: "{\"roles\":[\"HrManager\"]}"),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("{\"roles\":[\"Employee\"]}", item.PreviousAccess);
        Assert.Equal("{\"roles\":[\"HrManager\"]}", item.NewAccess);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_EventType_When_Summary_Is_Empty()
    {
        var now = DateTimeOffset.UtcNow;
        var reader = new FakeAuditHistoryReader().WithPlatformEntries(
        [
            Entry("user.roles-changed", now, summary: string.Empty),
        ]);

        var handler = BuildHandler(reader);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId }, CancellationToken.None);

        Assert.Equal("user.roles-changed", result.Items[0].Summary);
    }
}
