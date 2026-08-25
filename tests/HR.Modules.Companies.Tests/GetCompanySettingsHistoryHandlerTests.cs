using HR.Modules.Companies.Features.GetCompanySettingsHistory;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// SET-02: reuses IAuditHistoryReader.GetPlatformAuditLogAsync exactly as GetAuditLogHandlerTests
/// does — see its remarks — but always fixes companyId to the caller's own company (never null,
/// unlike the platform-admin audit log) and restricts eventType to "company-settings.updated".
/// </summary>
public class GetCompanySettingsHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Filters_By_EventType_CompanySettingsUpdated()
    {
        var companyId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: "Company settings updated",
                    BeforeJson: "{}", AfterJson: "{}", EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
                new AuditHistoryEntry(
                    Now, "hr-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: "HR settings updated",
                    BeforeJson: "{}", AfterJson: "{}", EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
            ],
        };

        var handler = new GetCompanySettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanySettingsHistoryRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("company-settings", item.Category);
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
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: targetCompanyId,
                    CorrelationId: null, CompanyId: targetCompanyId),
                new AuditHistoryEntry(
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: otherCompanyId,
                    CorrelationId: null, CompanyId: otherCompanyId),
            ],
        };

        var handler = new GetCompanySettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanySettingsHistoryRequest { CompanyId = targetCompanyId },
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
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: actorId, ActorEmployeeId: null, Summary: null,
                    BeforeJson: "{\"timeZone\":\"UTC\"}", AfterJson: "{\"timeZone\":\"Europe/London\"}",
                    EmployeeId: null, EntityId: companyId, CorrelationId: null, CompanyId: companyId),
            ],
        };

        var emailReader = new FakeUserEmailDirectoryReader
        {
            EmailsByUserId = new Dictionary<Guid, string> { [actorId] = "admin@example.com" },
        };

        var handler = new GetCompanySettingsHistoryHandler(auditReader, emailReader);

        var result = await handler.HandleAsync(
            new GetCompanySettingsHistoryRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(actorId, item.ActorUserId);
        Assert.Equal("admin@example.com", item.ActorEmail);
        Assert.Equal("{\"timeZone\":\"UTC\"}", item.PreviousValueJson);
        Assert.Equal("{\"timeZone\":\"Europe/London\"}", item.NewValueJson);
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
                    Now, "company-settings.updated", "CompanySettings",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: companyId,
                    CorrelationId: null, CompanyId: companyId),
            ],
        };

        var handler = new GetCompanySettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanySettingsHistoryRequest { CompanyId = companyId },
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
                Now.AddMinutes(-i), "company-settings.updated", "CompanySettings",
                ActorUserId: null, ActorEmployeeId: null, Summary: null,
                BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: companyId,
                CorrelationId: null, CompanyId: companyId))
            .ToList();

        var auditReader = new FakeAuditHistoryReader { PlatformEntries = entries };

        var handler = new GetCompanySettingsHistoryHandler(auditReader, new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetCompanySettingsHistoryRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(2, result.Value.PageSize);
    }
}
