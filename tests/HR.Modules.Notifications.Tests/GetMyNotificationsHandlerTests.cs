using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class GetMyNotificationsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static Notification Make(
        Guid companyId, Guid employeeId,
        string title = "A notification",
        bool isRead = false,
        NotificationPriority priority = NotificationPriority.Normal,
        DateTimeOffset? createdAt = null)
    {
        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            title, null, Guid.NewGuid(),
            createdAt ?? Now,
            priority: priority);
        if (isRead) n.MarkAsRead();
        return n;
    }

    [Fact]
    public async Task Returns_Only_Current_Employee_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherId    = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Mine"),
            Make(companyId, otherId,    "Not mine"));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Mine", item.Title);
    }

    [Fact]
    public async Task UnreadCount_Reflects_Only_Unread_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: true));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task Orders_By_CreatedAt_Descending()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Oldest", createdAt: Now.AddHours(-2)),
            Make(companyId, employeeId, "Newest", createdAt: Now),
            Make(companyId, employeeId, "Middle", createdAt: Now.AddHours(-1)));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Newest", result.Items[0].Title);
        Assert.Equal("Middle", result.Items[1].Title);
        Assert.Equal("Oldest", result.Items[2].Title);
    }

    [Fact]
    public async Task Maps_Priority_To_String()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.Add(Make(companyId, employeeId, priority: NotificationPriority.Urgent));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Urgent", Assert.Single(result.Items).Priority);
    }

    [Fact]
    public async Task Limits_Results_To_Default_Page_Size_Of_Fifty_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var notifications = Enumerable.Range(0, 51)
            .Select(i => Make(companyId, employeeId, $"Item {i}", createdAt: Now.AddMinutes(-i)))
            .ToArray();
        ctx.Notifications.AddRange(notifications);
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(50, result.Items.Count);
        Assert.Equal("Item 0", result.Items[0].Title);
        Assert.DoesNotContain(result.Items, i => i.Title == "Item 50");
        Assert.Equal(51, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task Returns_Empty_When_No_Notifications()
    {
        await using var ctx = BuildContext();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.UnreadCount);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    // ── NOT-06: UnreadCount independence from paging/filters ────────────────

    [Fact]
    public async Task UnreadCount_Is_Not_Capped_By_PageSize_When_More_Than_Fifty_Unread()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var notifications = Enumerable.Range(0, 63)
            .Select(i => Make(companyId, employeeId, $"Item {i}", createdAt: Now.AddMinutes(-i)))
            .ToArray();
        ctx.Notifications.AddRange(notifications);
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                PageSize = 20,
                PageNumber = 1,
            },
            CancellationToken.None);

        Assert.Equal(63, result.UnreadCount);
        Assert.Equal(20, result.Items.Count);
    }

    [Fact]
    public async Task UnreadCount_Only_Reflects_Callers_Own_Company_And_Employee_Scope()
    {
        await using var ctx = BuildContext();
        var companyA   = Guid.NewGuid();
        var companyB   = Guid.NewGuid();
        var employeeA  = Guid.NewGuid();
        var employeeB  = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyA, employeeA, isRead: false),
            Make(companyA, employeeA, isRead: false),
            Make(companyA, employeeA, isRead: true),
            Make(companyA, employeeB, isRead: false),
            Make(companyB, employeeA, isRead: false));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest
            {
                CompanyId = companyA,
                EmployeeId = employeeA,
                IsRead = true,
                PageSize = 1,
                PageNumber = 1,
            },
            CancellationToken.None);

        Assert.Equal(2, result.UnreadCount);
    }

    // ── NOT-06: deterministic tie-break ordering ─────────────────────────────

    [Fact]
    public async Task Orders_By_CreatedAt_Then_Id_Descending_When_Timestamps_Are_Equal()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var same = Now;
        var notifications = Enumerable.Range(0, 5)
            .Select(i => Notification.Create(
                Guid.Parse($"00000000-0000-0000-0000-00000000000{i}"),
                companyId, employeeId, $"Item {i}", null, Guid.NewGuid(), same))
            .ToArray();
        ctx.Notifications.AddRange(notifications);
        await ctx.SaveChangesAsync();

        var expectedOrder = notifications
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => n.Id)
            .ToList();

        var first = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);
        var second = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(expectedOrder, first.Items.Select(i => i.Id).ToList());
        Assert.Equal(expectedOrder, second.Items.Select(i => i.Id).ToList());
        Assert.Equal(first.Items.Select(i => i.Id).ToList(), second.Items.Select(i => i.Id).ToList());
    }

    // ── NOT-06: pagination correctness ───────────────────────────────────────

    [Fact]
    public async Task Paginates_Correctly_Across_Multiple_Pages()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var notifications = Enumerable.Range(0, 55)
            .Select(i => Make(companyId, employeeId, $"Item {i}", createdAt: Now.AddMinutes(-i)))
            .ToArray();
        ctx.Notifications.AddRange(notifications);
        await ctx.SaveChangesAsync();

        var handler = new GetMyNotificationsHandler(ctx);
        var page1 = await handler.HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, PageNumber = 1, PageSize = 20 },
            CancellationToken.None);
        var page2 = await handler.HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, PageNumber = 2, PageSize = 20 },
            CancellationToken.None);
        var page3 = await handler.HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, PageNumber = 3, PageSize = 20 },
            CancellationToken.None);

        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(20, page2.Items.Count);
        Assert.Equal(15, page3.Items.Count);
        Assert.Equal(55, page1.TotalCount);
        Assert.Equal(55, page2.TotalCount);
        Assert.Equal(55, page3.TotalCount);
        Assert.Equal(3, page1.TotalPages);

        var combinedIds = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3.Items.Select(i => i.Id))
            .ToList();
        Assert.Equal(55, combinedIds.Count);
        Assert.Equal(55, combinedIds.Distinct().Count());
    }

    // ── NOT-06: IsRead filter ─────────────────────────────────────────────────

    [Fact]
    public async Task Filters_By_IsRead_True_Returns_Only_Read_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Read 1", isRead: true),
            Make(companyId, employeeId, "Read 2", isRead: true),
            Make(companyId, employeeId, "Unread 1", isRead: false));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, IsRead = true },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.True(i.IsRead));
        Assert.Equal(1, result.UnreadCount);
    }

    [Fact]
    public async Task Filters_By_IsRead_False_Returns_Only_Unread_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Read 1", isRead: true),
            Make(companyId, employeeId, "Unread 1", isRead: false),
            Make(companyId, employeeId, "Unread 2", isRead: false));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, IsRead = false },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.False(i.IsRead));
        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task Omitting_IsRead_Filter_Returns_Both_Read_And_Unread()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Read 1", isRead: true),
            Make(companyId, employeeId, "Unread 1", isRead: false));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    // ── NOT-06: Type / Priority filters ──────────────────────────────────────

    [Fact]
    public async Task Filters_By_Type_Returns_Only_Matching_Rows()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Notification.Create(Guid.NewGuid(), companyId, employeeId, "Task", null, Guid.NewGuid(), Now, NotificationType.TaskAssigned),
            Notification.Create(Guid.NewGuid(), companyId, employeeId, "Leave", null, Guid.NewGuid(), Now, NotificationType.LeaveApproved));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, Type = NotificationType.LeaveApproved },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Leave", item.Title);
        Assert.Equal(NotificationType.LeaveApproved.ToString(), item.Type);
    }

    [Fact]
    public async Task Filters_By_Priority_Returns_Only_Matching_Rows()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Urgent one", priority: NotificationPriority.Urgent),
            Make(companyId, employeeId, "Low one", priority: NotificationPriority.Low));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, Priority = NotificationPriority.Urgent },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Urgent one", item.Title);
    }

    // ── NOT-06: CreatedFrom / CreatedTo range filter (inclusive boundaries) ──

    [Fact]
    public async Task CreatedFrom_And_CreatedTo_Filter_Includes_Boundary_Values()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var from = Now.AddDays(-2);
        var to   = Now;

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Before range", createdAt: from.AddSeconds(-1)),
            Make(companyId, employeeId, "At from boundary", createdAt: from),
            Make(companyId, employeeId, "In range", createdAt: from.AddDays(1)),
            Make(companyId, employeeId, "At to boundary", createdAt: to),
            Make(companyId, employeeId, "After range", createdAt: to.AddSeconds(1)));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId, CreatedFrom = from, CreatedTo = to },
            CancellationToken.None);

        var titles = result.Items.Select(i => i.Title).ToList();
        Assert.Equal(3, titles.Count);
        Assert.Contains("At from boundary", titles);
        Assert.Contains("In range", titles);
        Assert.Contains("At to boundary", titles);
        Assert.DoesNotContain("Before range", titles);
        Assert.DoesNotContain("After range", titles);
    }

    // ── NOT-06: combined filters + pagination ────────────────────────────────

    [Fact]
    public async Task Combines_IsRead_Type_DateRange_And_Pagination()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var from = Now.AddDays(-5);
        var to   = Now;

        // Matching set: unread, TaskAssigned, within range — 3 of them.
        for (var i = 0; i < 3; i++)
        {
            ctx.Notifications.Add(Notification.Create(
                Guid.NewGuid(), companyId, employeeId, $"Match {i}", null, Guid.NewGuid(),
                from.AddDays(i), NotificationType.TaskAssigned));
        }

        // Non-matching: read.
        var readOne = Notification.Create(
            Guid.NewGuid(), companyId, employeeId, "Read but otherwise matches", null, Guid.NewGuid(),
            from.AddDays(1), NotificationType.TaskAssigned);
        readOne.MarkAsRead();
        ctx.Notifications.Add(readOne);

        // Non-matching: wrong type.
        ctx.Notifications.Add(Notification.Create(
            Guid.NewGuid(), companyId, employeeId, "Wrong type", null, Guid.NewGuid(),
            from.AddDays(1), NotificationType.LeaveApproved));

        // Non-matching: outside range.
        ctx.Notifications.Add(Notification.Create(
            Guid.NewGuid(), companyId, employeeId, "Outside range", null, Guid.NewGuid(),
            from.AddDays(-1), NotificationType.TaskAssigned));

        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                IsRead = false,
                Type = NotificationType.TaskAssigned,
                CreatedFrom = from,
                CreatedTo = to,
                PageNumber = 1,
                PageSize = 2,
            },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.All(result.Items, i => Assert.StartsWith("Match", i.Title));
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
