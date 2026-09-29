using HR.Modules.Companies.Contracts;
using HR.Modules.Notifications.Features.PreviewProductUpdateRecipients;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features;

public class PreviewProductUpdateRecipientsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_RecipientCount_And_Distinct_CompanyCount_Matching_Resolver()
    {
        var activeCompanies = new FakeActiveCompanyDirectory();
        var subscriptions = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();

        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyA, companyB];
        admins.AdminEmployeeIdsByCompany[companyA] = [Guid.NewGuid()];
        admins.AdminEmployeeIdsByCompany[companyB] = [Guid.NewGuid(), Guid.NewGuid()];

        var handler = new PreviewProductUpdateRecipientsHandler(activeCompanies, subscriptions, admins);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(3, result.Value!.RecipientCount);
        Assert.Equal(2, result.Value!.CompanyCount);
    }

    [Fact]
    public async Task HandleAsync_CompanyCount_Counts_Distinct_Companies_Only()
    {
        // A company with zero eligible admins must not inflate CompanyCount even though it is
        // itself an active, eligible company.
        var activeCompanies = new FakeActiveCompanyDirectory();
        var subscriptions = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();

        var companyWithAdmins = Guid.NewGuid();
        var companyWithoutAdmins = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyWithAdmins, companyWithoutAdmins];
        admins.AdminEmployeeIdsByCompany[companyWithAdmins] = [Guid.NewGuid()];
        admins.AdminEmployeeIdsByCompany[companyWithoutAdmins] = [];

        var handler = new PreviewProductUpdateRecipientsHandler(activeCompanies, subscriptions, admins);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(1, result.Value!.RecipientCount);
        Assert.Equal(1, result.Value!.CompanyCount);
    }

    [Fact]
    public async Task HandleAsync_Never_Writes_A_Notification()
    {
        var ctx = BuildContext();
        var activeCompanies = new FakeActiveCompanyDirectory();
        var subscriptions = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();

        var companyId = Guid.NewGuid();
        activeCompanies.ActiveCompanyIds = [companyId];
        admins.AdminEmployeeIdsByCompany[companyId] = [Guid.NewGuid()];

        var handler = new PreviewProductUpdateRecipientsHandler(activeCompanies, subscriptions, admins);

        await handler.HandleAsync(CancellationToken.None);

        Assert.Empty(await ctx.Notifications.ToListAsync());
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
