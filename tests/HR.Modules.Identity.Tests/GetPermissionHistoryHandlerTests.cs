using HR.Modules.Identity.Features.GetPermissionHistory;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: unit tests for <see cref="GetPermissionHistoryHandler"/>.
/// Uses FakeAuditHistoryReader so no database or Docker required.
/// </summary>
public class GetPermissionHistoryHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static AuditHistoryEntry MakeEntry(
        string eventType,
        Guid? actorUserId = null,
        Guid? employeeId = null,
        DateTimeOffset? occurredAt = null,
        string? beforeJson = null,
        string? afterJson = null) =>
        new(
            OccurredAt: occurredAt ?? T0,
            EventType: eventType,
            EntityType: "user",
            ActorUserId: actorUserId,
            ActorEmployeeId: null,
            Summary: $"Test {eventType}",
            BeforeJson: beforeJson,
            AfterJson: afterJson,
            EmployeeId: employeeId,
            CompanyId: CompanyId);

    private static GetPermissionHistoryHandler BuildHandler(
        IReadOnlyList<AuditHistoryEntry> platformEntries,
        FakeEmployeeNameReader? nameReader = null) =>
        new(
            new FakeAuditHistoryReader().WithPlatformEntries(platformEntries),
            nameReader ?? new FakeEmployeeNameReader());

    [Fact]
    public async Task HandleAsync_Returns_Only_Permission_Event_Types()
    {
        var permissionEntry = MakeEntry("user.roles-changed");
        var unrelatedEntry  = MakeEntry("employee.profile-updated");

        var handler = BuildHandler([permissionEntry, unrelatedEntry]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items, i => i.EventType == "user.roles-changed");
    }

    [Fact]
    public async Task HandleAsync_Includes_Position_Role_And_Override_Events_Alongside_Direct_Role_Changes()
    {
        var direct   = MakeEntry("user.roles-changed");
        var position = MakeEntry("position.role-defaults-changed");
        var over     = MakeEntry("user.role-override-created");
        var unrelated= MakeEntry("leave.request-submitted");

        var handler = BuildHandler([direct, position, over, unrelated]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Contains(result.Items, i => i.EventType == "user.roles-changed");
        Assert.Contains(result.Items, i => i.EventType == "position.role-defaults-changed");
        Assert.Contains(result.Items, i => i.EventType == "user.role-override-created");
    }

    [Fact]
    public async Task HandleAsync_Surfaces_Actor_Name_When_ActorUserId_Is_Present()
    {
        var actorId = Guid.NewGuid();
        var entry   = MakeEntry("user.roles-changed", actorUserId: actorId);
        var names   = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [actorId] = "Alice Admin" });

        var handler = BuildHandler([entry], names);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Alice Admin", item.PerformedBy);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_System_When_ActorUserId_Is_Null()
    {
        var entry = MakeEntry("user.roles-changed", actorUserId: null);
        var handler = BuildHandler([entry]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("System", item.PerformedBy);
    }

    [Fact]
    public async Task HandleAsync_Maps_Before_And_After_Json_As_Previous_And_New_Access()
    {
        var entry = MakeEntry("user.roles-changed", beforeJson: "{\"roles\":[\"Employee\"]}", afterJson: "{\"roles\":[\"HrManager\"]}");
        var handler = BuildHandler([entry]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("{\"roles\":[\"Employee\"]}", item.PreviousAccess);
        Assert.Equal("{\"roles\":[\"HrManager\"]}", item.NewAccess);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EmployeeId_When_Provided()
    {
        var target = Guid.NewGuid();
        var other  = Guid.NewGuid();

        var forTarget = MakeEntry("user.roles-changed", employeeId: target);
        var forOther  = MakeEntry("user.roles-changed", employeeId: other);

        var handler = BuildHandler([forTarget, forOther]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, EmployeeId = target, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal(target, i.TargetEmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Returns_Items_In_Descending_OccurredAt_Order()
    {
        var earlier = MakeEntry("user.roles-changed", occurredAt: T0.AddHours(-2));
        var later   = MakeEntry("user.role-override-created", occurredAt: T0);

        var handler = BuildHandler([earlier, later]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(later.OccurredAt, result.Items[0].OccurredAt);
        Assert.Equal(earlier.OccurredAt, result.Items[1].OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Paginates_Results()
    {
        var entries = Enumerable.Range(0, 10)
            .Select(i => MakeEntry("user.roles-changed", occurredAt: T0.AddMinutes(i)))
            .ToList();

        var handler = BuildHandler(entries);

        var page1 = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 5 },
            CancellationToken.None);

        var page2 = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 2, PageSize = 5 },
            CancellationToken.None);

        Assert.Equal(10, page1.TotalCount);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(10, page2.TotalCount);
        Assert.Equal(5, page2.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Includes_Permission_Denied_Events()
    {
        var denialEntry = MakeEntry("user.permission-denied");
        var handler = BuildHandler([denialEntry]);

        var result = await handler.HandleAsync(
            new GetPermissionHistoryRequest { CompanyId = CompanyId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items, i => i.EventType == "user.permission-denied");
    }
}
