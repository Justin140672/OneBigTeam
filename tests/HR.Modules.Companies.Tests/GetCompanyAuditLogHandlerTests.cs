using HR.Modules.Companies.Features.GetCompanyAuditLog;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// AUD-05: unit tests for <see cref="GetCompanyAuditLogHandler"/>.
/// Uses FakeAuditHistoryReader / FakeUserEmailDirectoryReader in place of real infrastructure.
/// </summary>
public class GetCompanyAuditLogHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ActorId   = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly DateTimeOffset T0 = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 1, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 1, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Paginated_Entries_For_Company()
    {
        var entries = new[]
        {
            MakeEntry("employee.profile-updated", T2, EmployeeId),
            MakeEntry("leave.requested",           T1, EmployeeId),
            MakeEntry("employee.profile-updated", T0, EmployeeId),
        };

        var handler = BuildHandler(entries, emails: [(ActorId, "hr@acme.com")]);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest { CompanyId = CompanyId, PageNumber = 1, PageSize = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.Equal(3, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EventType()
    {
        var entries = new[]
        {
            MakeEntry("employee.profile-updated", T2, EmployeeId),
            MakeEntry("leave.requested",           T1, EmployeeId),
        };

        var handler = BuildHandler(entries);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest
            {
                CompanyId = CompanyId,
                EventType = "leave.requested",
                PageNumber = 1, PageSize = 10,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("leave.requested", result.Value.Items[0].EventType);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EmployeeId()
    {
        var otherEmployee = Guid.NewGuid();
        var entries = new[]
        {
            MakeEntry("employee.profile-updated", T2, EmployeeId),
            MakeEntry("employee.profile-updated", T1, otherEmployee),
        };

        var handler = BuildHandler(entries);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest
            {
                CompanyId = CompanyId,
                EmployeeId = EmployeeId,
                PageNumber = 1, PageSize = 10,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(EmployeeId, result.Value.Items[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Date_Range()
    {
        var entries = new[]
        {
            MakeEntry("employee.profile-updated", T0, EmployeeId),
            MakeEntry("employee.profile-updated", T1, EmployeeId),
            MakeEntry("employee.profile-updated", T2, EmployeeId),
        };

        var handler = BuildHandler(entries);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest
            {
                CompanyId = CompanyId,
                FromDate  = T1,
                ToDate    = T1,
                PageNumber = 1, PageSize = 10,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(T1, result.Value.Items[0].OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Actor_Email()
    {
        var entries = new[] { MakeEntry("employee.profile-updated", T0, EmployeeId) };
        var handler = BuildHandler(entries, emails: [(ActorId, "hr@acme.com")]);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest { CompanyId = CompanyId, PageNumber = 1, PageSize = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hr@acme.com", result.Value!.Items[0].ActorDisplayName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_ActorDisplayName_When_Actor_Is_Not_A_User()
    {
        var entry = new AuditHistoryEntry(T0, "employee.profile-updated", "Employee", null, null, "summary", null, null, EmployeeId, default);
        var reader = new FakeAuditHistoryReader { PlatformEntries = [entry with { CompanyId = CompanyId }] };
        var handler = new GetCompanyAuditLogHandler(reader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest { CompanyId = CompanyId, PageNumber = 1, PageSize = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Items[0].ActorDisplayName);
    }

    [Fact]
    public async Task HandleAsync_Paginates_Results()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => MakeEntry("employee.profile-updated", T0.AddHours(i), EmployeeId))
            .ToArray();

        var handler = BuildHandler(entries);

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest { CompanyId = CompanyId, PageNumber = 1, PageSize = 2 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Entries_From_Other_Company()
    {
        var otherCompanyEntry = new AuditHistoryEntry(T0, "employee.profile-updated", "Employee",
            ActorId, null, "summary", null, null, EmployeeId, default, CompanyId: Guid.NewGuid());
        var reader = new FakeAuditHistoryReader { PlatformEntries = [otherCompanyEntry] };
        var handler = new GetCompanyAuditLogHandler(reader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanyAuditLogRequest { CompanyId = CompanyId, PageNumber = 1, PageSize = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private AuditHistoryEntry MakeEntry(string eventType, DateTimeOffset at, Guid employeeId) =>
        new(at, eventType, "Employee", ActorId, null, "summary", null, null, employeeId, default, CompanyId: CompanyId);

    private static GetCompanyAuditLogHandler BuildHandler(
        IReadOnlyList<AuditHistoryEntry> entries,
        IEnumerable<(Guid id, string email)>? emails = null)
    {
        var reader = new FakeAuditHistoryReader { PlatformEntries = entries };
        var emailReader = new FakeUserEmailDirectoryReader
        {
            EmailsByUserId = emails?.ToDictionary(e => e.id, e => e.email)
                ?? new Dictionary<Guid, string>(),
        };
        return new GetCompanyAuditLogHandler(reader, emailReader);
    }
}
