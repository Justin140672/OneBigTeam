using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetAuditLog;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// Same "platform-wide admin dashboard with allow-list gate" shape as GetFailedPaymentsHandlerTests /
/// ListCustomersHandlerTests — see their remarks. Uses an in-memory CompaniesDbContext (for company
/// name resolution) plus FakeAuditHistoryReader/FakeUserEmailDirectoryReader in place of the real
/// cross-cutting AuditDbContext / IdentityDbContext-backed readers.
/// </summary>
public class GetAuditLogHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakeAuditHistoryReader(),
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(new GetAuditLogRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Is_Null()
    {
        await using var context = BuildContext();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: null),
            BuildConfiguration("admin@example.com"),
            new FakeAuditHistoryReader(),
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(new GetAuditLogRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Maps_Entries_And_Resolves_Company_Name_And_Administrator_Email()
    {
        await using var context = BuildContext();

        var company = Company.Create(Guid.NewGuid(), "Linked Co", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: actorId, ActorEmployeeId: null, Summary: "Trial extended",
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: company.Id,
                    CorrelationId: null, CompanyId: company.Id),
            ],
        };

        var emailReader = new FakeUserEmailDirectoryReader
        {
            EmailsByUserId = new Dictionary<Guid, string> { [actorId] = "admin@example.com" },
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            emailReader);

        var result = await handler.HandleAsync(new GetAuditLogRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(AuditLogActionTypes.TrialExtended, item.EventType);
        Assert.Equal(company.Id, item.CompanyId);
        Assert.Equal("Linked Co", item.CompanyName);
        Assert.Equal(actorId, item.ActorUserId);
        Assert.Equal("admin@example.com", item.AdministratorEmail);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(AuditLogActionTypes.All, result.Value.AvailableEventTypes);
    }

    [Fact]
    public async Task HandleAsync_Maps_PlatformWide_Entry_With_No_Company_To_Null_CompanyId_And_Name()
    {
        await using var context = BuildContext();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.BackgroundJobAdminRetried, "BackgroundJob",
                    ActorUserId: null, ActorEmployeeId: null, Summary: "Job retried",
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.Empty),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(new GetAuditLogRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.CompanyId);
        Assert.Null(item.CompanyName);
        Assert.Null(item.ActorUserId);
        Assert.Null(item.AdministratorEmail);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Page_When_AdministratorEmail_Matches_Nobody()
    {
        await using var context = BuildContext();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: Guid.NewGuid(), ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.NewGuid()),
            ],
        };

        var emailReader = new FakeUserEmailDirectoryReader
        {
            UserIdsToReturnForEmailSearch = [],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            emailReader);

        var result = await handler.HandleAsync(
            new GetAuditLogRequest { AdministratorEmail = "nobody@example.com" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(0, result.Value.TotalPages);
        Assert.Equal("nobody@example.com", emailReader.LastEmailSearchTerm);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_AdministratorEmail_When_Matches_Are_Found()
    {
        await using var context = BuildContext();

        var matchingActorId = Guid.NewGuid();
        var otherActorId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: matchingActorId, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.NewGuid()),
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: otherActorId, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.NewGuid()),
            ],
        };

        var emailReader = new FakeUserEmailDirectoryReader
        {
            UserIdsToReturnForEmailSearch = [matchingActorId],
            EmailsByUserId = new Dictionary<Guid, string> { [matchingActorId] = "matched@example.com" },
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            emailReader);

        var result = await handler.HandleAsync(
            new GetAuditLogRequest { AdministratorEmail = "matched" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matchingActorId, item.ActorUserId);
        Assert.Equal("matched@example.com", item.AdministratorEmail);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_CompanyId()
    {
        await using var context = BuildContext();

        var targetCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: targetCompanyId,
                    CorrelationId: null, CompanyId: targetCompanyId),
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: otherCompanyId,
                    CorrelationId: null, CompanyId: otherCompanyId),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetAuditLogRequest { CompanyId = targetCompanyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(targetCompanyId, item.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EventType()
    {
        await using var context = BuildContext();

        var auditReader = new FakeAuditHistoryReader
        {
            PlatformEntries =
            [
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.NewGuid()),
                new AuditHistoryEntry(
                    Now, AuditLogActionTypes.SupportSessionGenerated, "SupportSession",
                    ActorUserId: null, ActorEmployeeId: null, Summary: null,
                    BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                    CorrelationId: null, CompanyId: Guid.NewGuid()),
            ],
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetAuditLogRequest { EventType = AuditLogActionTypes.SupportSessionGenerated },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(AuditLogActionTypes.SupportSessionGenerated, item.EventType);
    }

    [Fact]
    public async Task HandleAsync_Applies_Paging()
    {
        await using var context = BuildContext();

        var entries = Enumerable.Range(0, 5)
            .Select(i => new AuditHistoryEntry(
                Now.AddMinutes(-i), AuditLogActionTypes.TrialExtended, "CustomerSubscription",
                ActorUserId: null, ActorEmployeeId: null, Summary: null,
                BeforeJson: null, AfterJson: null, EmployeeId: null, EntityId: Guid.NewGuid(),
                CorrelationId: null, CompanyId: Guid.NewGuid()))
            .ToList();

        var auditReader = new FakeAuditHistoryReader { PlatformEntries = entries };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            auditReader,
            new FakeUserEmailDirectoryReader());

        var result = await handler.HandleAsync(
            new GetAuditLogRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(2, result.Value.PageNumber);
    }

    private static GetAuditLogHandler BuildHandler(
        CompaniesDbContext context,
        ICurrentUser currentUser,
        IConfiguration configuration,
        FakeAuditHistoryReader auditHistoryReader,
        FakeUserEmailDirectoryReader userEmailDirectoryReader)
    {
        return new GetAuditLogHandler(
            context,
            currentUser,
            configuration,
            auditHistoryReader,
            userEmailDirectoryReader);
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            builder.AddInMemoryCollection(data);
        }
        else
        {
            builder.AddInMemoryCollection();
        }

        return builder.Build();
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
