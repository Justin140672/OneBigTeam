using HR.Modules.Companies.Contracts;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Tests.Infrastructure;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// Customer Release Notifications: eligibility-branch coverage for ProductUpdateRecipientResolver,
/// the single source of truth shared by PreviewProductUpdateRecipients and SendProductUpdate.
/// </summary>
public class ProductUpdateRecipientResolverTests
{
    [Fact]
    public async Task ResolveAsync_Excludes_Company_Not_In_Active_Company_Ids()
    {
        var companyId = Guid.NewGuid();
        var adminId    = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [] };
        var subscriptions   = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins          = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyId] = [adminId];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_Excludes_Company_With_TrialExpired_Subscription()
    {
        var companyId = Guid.NewGuid();
        var adminId    = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyId] };
        var subscriptions   = new FakeSubscriptionStatusReader();
        subscriptions.Statuses[companyId] = SubscriptionStatus.TrialExpired;
        var admins = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyId] = [adminId];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_Excludes_Company_With_Canceled_Subscription()
    {
        var companyId = Guid.NewGuid();
        var adminId    = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyId] };
        var subscriptions   = new FakeSubscriptionStatusReader();
        subscriptions.Statuses[companyId] = SubscriptionStatus.Canceled;
        var admins = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyId] = [adminId];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Paused)]
    public async Task ResolveAsync_Includes_Company_With_NonExpired_NonCanceled_Subscription(SubscriptionStatus status)
    {
        var companyId = Guid.NewGuid();
        var adminId    = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyId] };
        var subscriptions   = new FakeSubscriptionStatusReader();
        subscriptions.Statuses[companyId] = status;
        var admins = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyId] = [adminId];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        var recipient = Assert.Single(result);
        Assert.Equal(companyId, recipient.CompanyId);
        Assert.Equal(adminId, recipient.EmployeeId);
    }

    [Fact]
    public async Task ResolveAsync_Company_With_Zero_Admins_Contributes_No_Recipients()
    {
        var companyId = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyId] };
        var subscriptions   = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyId] = [];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_Multiple_Companies_Each_Contribute_Their_Own_Admins()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var adminA1  = Guid.NewGuid();
        var adminA2  = Guid.NewGuid();
        var adminB1  = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyA, companyB] };
        var subscriptions   = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();
        admins.AdminEmployeeIdsByCompany[companyA] = [adminA1, adminA2];
        admins.AdminEmployeeIdsByCompany[companyB] = [adminB1];

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.CompanyId == companyA && r.EmployeeId == adminA1);
        Assert.Contains(result, r => r.CompanyId == companyA && r.EmployeeId == adminA2);
        Assert.Contains(result, r => r.CompanyId == companyB && r.EmployeeId == adminB1);
    }

    [Fact]
    public async Task ResolveAsync_Does_Not_Fall_Back_To_Hr_Administrators()
    {
        // ICompanyAdministratorDirectory is the only recipient source consulted — an HR
        // Administrator with no Company Administrator role must never be resolved even though a
        // separate IHrAdministratorDirectory exists elsewhere in the codebase.
        var companyId = Guid.NewGuid();

        var activeCompanies = new FakeActiveCompanyDirectory { ActiveCompanyIds = [companyId] };
        var subscriptions   = new FakeSubscriptionStatusReader { DefaultStatus = SubscriptionStatus.Active };
        var admins = new FakeCompanyAdministratorDirectory();
        // No entry seeded for companyId — simulates a company whose only privileged users are HR
        // Administrators, not Company Administrators.

        var result = await ProductUpdateRecipientResolver.ResolveAsync(
            activeCompanies, subscriptions, admins, CancellationToken.None);

        Assert.Empty(result);
    }
}
