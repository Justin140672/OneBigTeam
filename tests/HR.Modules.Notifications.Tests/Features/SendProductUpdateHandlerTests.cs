using HR.Modules.Companies.Contracts;
using HR.Modules.Notifications.Features.SendProductUpdate;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features;

public class SendProductUpdateHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static (SendProductUpdateHandler Handler, NotificationsDbContext Ctx, FakeAuditPublisher Audit,
        FakeActiveCompanyDirectory ActiveCompanies, FakeSubscriptionStatusReader Subscriptions,
        FakeCompanyAdministratorDirectory Admins) Build()
    {
        var ctx = BuildContext();
        var writer = new NotificationWriter(
            ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var audit = new FakeAuditPublisher();
        var activeCompanies = new FakeActiveCompanyDirectory();
        var subscriptions = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();

        var handler = new SendProductUpdateHandler(
            activeCompanies, subscriptions, admins, writer, audit,
            new FakeCurrentUser(Guid.NewGuid()), new FakeClock(Now.UtcDateTime));

        return (handler, ctx, audit, activeCompanies, subscriptions, admins);
    }

    [Fact]
    public async Task HandleAsync_Writes_One_Notification_Per_Resolved_Recipient_With_Shared_BatchId()
    {
        var (handler, ctx, _, activeCompanies, _, admins) = Build();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var adminA   = Guid.NewGuid();
        var adminB1  = Guid.NewGuid();
        var adminB2  = Guid.NewGuid();

        activeCompanies.ActiveCompanyIds = [companyA, companyB];
        admins.AdminEmployeeIdsByCompany[companyA] = [adminA];
        admins.AdminEmployeeIdsByCompany[companyB] = [adminB1, adminB2];

        var response = await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", "/reports/x"), CancellationToken.None);

        Assert.True(response.IsSuccess);
        var saved = await ctx.Notifications.ToListAsync();
        Assert.Equal(3, saved.Count);
        Assert.All(saved, n => Assert.Equal(NotificationType.ProductUpdate, n.Type));

        var batchIds = saved.Select(n => n.SourceEntityId).Distinct().ToList();
        Assert.Single(batchIds);
    }

    [Fact]
    public async Task HandleAsync_Passes_Request_Url_Through_As_ActionUrl_Override()
    {
        var (handler, ctx, _, activeCompanies, _, admins) = Build();

        var companyId = Guid.NewGuid();
        var adminId   = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyId];
        admins.AdminEmployeeIdsByCompany[companyId] = [adminId];

        await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", "/reports/recruitment-pipeline"),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal("/reports/recruitment-pipeline", saved.ActionUrl);
    }

    [Fact]
    public async Task HandleAsync_Uses_Recipients_Own_CompanyId_Not_CrossCompany()
    {
        var (handler, ctx, _, activeCompanies, _, admins) = Build();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var adminA   = Guid.NewGuid();
        var adminB   = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyA, companyB];
        admins.AdminEmployeeIdsByCompany[companyA] = [adminA];
        admins.AdminEmployeeIdsByCompany[companyB] = [adminB];

        await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", null), CancellationToken.None);

        var saved = await ctx.Notifications.ToListAsync();
        var rowForA = Assert.Single(saved, n => n.EmployeeId == adminA);
        var rowForB = Assert.Single(saved, n => n.EmployeeId == adminB);
        Assert.Equal(companyA, rowForA.CompanyId);
        Assert.Equal(companyB, rowForB.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Exactly_One_ProductUpdateSentAuditEvent_With_Correct_Counts()
    {
        var ctx = BuildContext();
        var writer = new NotificationWriter(
            ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var audit = new FakeAuditPublisher();
        var activeCompanies = new FakeActiveCompanyDirectory();
        var subscriptions = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();
        var handler = new SendProductUpdateHandler(
            activeCompanies, subscriptions, admins, writer, audit,
            new FakeCurrentUser(Guid.NewGuid()), new FakeClock(Now.UtcDateTime));

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyA, companyB];
        admins.AdminEmployeeIdsByCompany[companyA] = [Guid.NewGuid()];
        admins.AdminEmployeeIdsByCompany[companyB] = [Guid.NewGuid(), Guid.NewGuid()];

        await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", null), CancellationToken.None);

        var published = Assert.Single(audit.Published);
        var evt = Assert.IsType<ProductUpdateSentAuditEvent>(published);
        Assert.Equal(3, evt.RecipientCount);
        Assert.Equal(2, evt.CompanyCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Response_Matching_What_Was_Written()
    {
        var (handler, _, _, activeCompanies, _, admins) = Build();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyA, companyB];
        admins.AdminEmployeeIdsByCompany[companyA] = [Guid.NewGuid()];
        admins.AdminEmployeeIdsByCompany[companyB] = [Guid.NewGuid(), Guid.NewGuid()];

        var response = await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", null), CancellationToken.None);

        Assert.Equal(3, response.Value!.RecipientCount);
        Assert.Equal(2, response.Value!.CompanyCount);
    }

    [Fact]
    public async Task HandleAsync_Zero_Recipients_Writes_Nothing_But_Returns_Zero_Response_And_Publishes_Audit_Event()
    {
        var (handler, ctx, audit, activeCompanies, _, _) = Build();
        activeCompanies.ActiveCompanyIds = [];

        var response = await handler.HandleAsync(
            new SendProductUpdateRequest("Release notes", "Big update!", null), CancellationToken.None);

        Assert.Equal(0, response.Value!.RecipientCount);
        Assert.Equal(0, response.Value!.CompanyCount);
        Assert.Empty(ctx.Notifications);

        var published = Assert.Single(audit.Published);
        var evt = Assert.IsType<ProductUpdateSentAuditEvent>(published);
        Assert.Equal(0, evt.RecipientCount);
        Assert.Equal(0, evt.CompanyCount);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
