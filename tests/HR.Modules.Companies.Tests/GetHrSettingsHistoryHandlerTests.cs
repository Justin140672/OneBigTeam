using HR.Modules.Companies.Features.GetHrSettingsHistory;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// SET-02 counterpart to GetCompanySettingsHistoryHandlerTests — same shape, but scoped to the
/// "hr-settings.updated" event type instead.
/// </summary>
public class GetHrSettingsHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Filters_By_EventType_HrSettingsUpdated()
    {
        var companyId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: "HR settings updated",
                    BeforeJson: "{}", AfterJson: "{}", EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
                new AuditHistoryEntry(
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: "Company settings updated",
                    BeforeJson: "{}", AfterJson: "{}", EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
            ],
        };

        var handler = new GetHrSettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetHrSettingsHistoryRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("hr-settings", item.Category);
    }

    [Fact]
    public async Task HandleAsync_Scopes_Results_To_The_Requested_CompanyId_Only()
    {
        var targetCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: targetCompanyId,
                    CorrelationId: null, CompanyId: targetCompanyId),
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: otherCompanyId,
                    CorrelationId: null, CompanyId: otherCompanyId),
            ],
        };

        var handler = new GetHrSettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetHrSettingsHistoryRequest { CompanyId = targetCompanyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Resolves_ActorEmail_Via_UserEmailDirectoryReader()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: actorId, ActorEmployeeId: null, Summary: null,
                    BeforeJson: "{\"probationMonths\":6}", AfterJson: "{\"probationMonths\":3}",
                    EmployeeId: null, EntityId: companyId, CorrelationId: null, CompanyId: companyId),
            ],
        };

        var emailReader = new FakeUserEmailDirectoryReader
        {
            EmailsByUserId = new Dictionary<Guid, string> { [actorId] = "hr-admin@example.com" },
        };

        var handler = new GetHrSettingsHistoryHandler(auditReader, emailReader);

        var result = await handler.HandleAsync(
            new GetHrSettingsHistoryRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(actorId, item.ActorUserId);
        Assert.Equal("hr-admin@example.com", item.ActorEmail);
        Assert.Equal("{\"probationMonths\":6}", item.PreviousValueJson);
        Assert.Equal("{\"probationMonths\":3}", item.NewValueJson);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_ActorEmail_When_Entry_Has_No_ActorUserId()
    {
        var companyId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
            ],
        };

        var handler = new GetHrSettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetHrSettingsHistoryRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.ActorUserId);
        Assert.Null(item.ActorEmail);
    }

    [Fact]
    public async Task HandleAsync_Passes_Pagination_Fields_Through()
    {
        var companyId = Guid.NewGuid();

        var entries = Enumerable.Range(0, 5)
            .Select(i => new AuditHistoryEntry(
                Now.AddMinutes(-i), "hr-settings.updated", "CompanySettings",
                ActorUserId: null, ActorEmployeeId: null, Summary: null,
                BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: companyId,
                CorrelationId: null, CompanyId: companyId))
            .ToList();

        var auditReader = new FakeAuditHistoryReader { PlatformEntries = entries };

        var handler = new GetHrSettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetHrSettingsHistoryRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(2, result.Value.PageSize);
    }
}
