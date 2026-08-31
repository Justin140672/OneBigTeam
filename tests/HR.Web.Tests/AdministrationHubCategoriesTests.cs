using HR.Web.Components.Pages.Administration;

namespace HR.Web.Tests;

/// <summary>
/// ADM-04 — the Administration hub shows a category card only when the signed-in user holds the
/// matching capability. The visibility mapping is a pure function
/// (<see cref="AdministrationHubCategories.VisibleTitles"/>) so it can be pinned per-persona here
/// without bUnit. The persona capability sets mirror the ADM-05 role-separation matrix.
/// </summary>
public class AdministrationHubCategoriesTests
{
    private const string CompanyProfile = "Company profile and addresses";
    private const string CompanyDefaults = "Company defaults";
    private const string Leave = "Leave";
    private const string Recruitment = "Recruitment";
    private const string Documents = "Documents";
    private const string Notifications = "Notifications";
    private const string Probation = "Probation";
    private const string Subscription = "Subscription";

    private static HubCapabilities Caps(
        bool canManageCompany = false,
        bool canManageCompanyConfiguration = false,
        bool canManageEmployees = false,
        bool canManageLeavePolicies = false,
        bool canManageRecruitment = false,
        bool canManageSharedDocuments = false,
        bool canManageHrSettings = false,
        bool isHrAdministrator = false) =>
        new(canManageCompany, canManageCompanyConfiguration, canManageEmployees, canManageLeavePolicies,
            canManageRecruitment, canManageSharedDocuments, canManageHrSettings, isHrAdministrator);

    [Fact]
    public void CompanyAdministratorAlone_SeesOnlyCompanyAndSubscriptionCategories()
    {
        var visible = AdministrationHubCategories.VisibleTitles(
            Caps(canManageCompany: true, canManageCompanyConfiguration: true));

        Assert.Equal(new[] { CompanyProfile, CompanyDefaults, Subscription }, visible);

        // ADM-05: none of the HR / recruitment surface leaks in for a company-config-only persona.
        Assert.DoesNotContain(Leave, visible);
        Assert.DoesNotContain(Recruitment, visible);
        Assert.DoesNotContain(Documents, visible);
        Assert.DoesNotContain(Notifications, visible);
        Assert.DoesNotContain(Probation, visible);
    }

    [Fact]
    public void HrAdministrator_SeesFullHrSurface_ButNotRecruitmentOrCompanyConfig()
    {
        var visible = AdministrationHubCategories.VisibleTitles(
            Caps(canManageHrSettings: true, canManageEmployees: true, canManageLeavePolicies: true,
                canManageSharedDocuments: true, isHrAdministrator: true));

        // ADM-05: HR Administrator does NOT hold company:manage, so the Company profile / Company
        // defaults categories stay hidden; Subscription shows via the IsHrAdministrator disjunct.
        Assert.Equal(
            new[] { Leave, Documents, Notifications, Probation, Subscription },
            visible);
        Assert.DoesNotContain(Recruitment, visible);
        Assert.DoesNotContain(CompanyProfile, visible);
        Assert.DoesNotContain(CompanyDefaults, visible);
    }

    [Fact]
    public void Recruiter_SeesOnlyRecruitment()
    {
        var visible = AdministrationHubCategories.VisibleTitles(Caps(canManageRecruitment: true));

        Assert.Equal(new[] { Recruitment }, visible);
    }

    [Fact]
    public void Manager_WithNoManageCapabilities_SeesNothing()
    {
        var visible = AdministrationHubCategories.VisibleTitles(Caps());

        Assert.Empty(visible);
    }

    [Fact]
    public void CompanyAdministratorPlusRecruiter_SeesCompanyAndRecruitmentCategories()
    {
        var visible = AdministrationHubCategories.VisibleTitles(
            Caps(canManageCompany: true, canManageCompanyConfiguration: true, canManageRecruitment: true));

        Assert.Equal(new[] { CompanyProfile, CompanyDefaults, Recruitment, Subscription }, visible);
        Assert.DoesNotContain(Leave, visible);
        Assert.DoesNotContain(Documents, visible);
    }

    [Fact]
    public void Leave_IsVisibleFromEitherDisjunct_Independently()
    {
        Assert.Contains(Leave, AdministrationHubCategories.VisibleTitles(Caps(canManageEmployees: true)));
        Assert.Contains(Leave, AdministrationHubCategories.VisibleTitles(Caps(canManageLeavePolicies: true)));
    }

    [Fact]
    public void Documents_IsVisibleFromEitherDisjunct_Independently()
    {
        Assert.Contains(Documents, AdministrationHubCategories.VisibleTitles(Caps(canManageEmployees: true)));
        Assert.Contains(Documents, AdministrationHubCategories.VisibleTitles(Caps(canManageSharedDocuments: true)));
    }

    [Fact]
    public void Subscription_IsVisibleFromEitherDisjunct_Independently()
    {
        Assert.Contains(Subscription, AdministrationHubCategories.VisibleTitles(Caps(canManageCompany: true)));
        Assert.Contains(Subscription, AdministrationHubCategories.VisibleTitles(Caps(isHrAdministrator: true)));
    }

    [Fact]
    public void CompanyDefaults_NumberingLink_IsNotYetConfigurable_WithoutHrSettings()
    {
        var categories = AdministrationHubCategories.Build(Caps(canManageCompany: true), Guid.NewGuid());
        var defaults = categories.Single(c => c.Title == CompanyDefaults);

        Assert.Contains(defaults.Links, l => l.Text == "Employee & asset numbering" && l.Url is null);
    }

    [Fact]
    public void CompanyDefaults_NumberingLink_ResolvesToHrSettings_WhenHrSettingsHeld()
    {
        var companyId = Guid.NewGuid();
        var categories = AdministrationHubCategories.Build(
            Caps(canManageCompany: true, canManageHrSettings: true), companyId);
        var defaults = categories.Single(c => c.Title == CompanyDefaults);

        Assert.Contains(defaults.Links,
            l => l.Text == "Employee & asset numbering" && l.Url == $"/companies/{companyId}/hr-settings");
    }

    [Fact]
    public void Notifications_AlwaysHasANotYetConfigurableMarker()
    {
        var categories = AdministrationHubCategories.Build(Caps(canManageHrSettings: true), Guid.NewGuid());
        var notifications = categories.Single(c => c.Title == Notifications);

        Assert.All(notifications.Links, l => Assert.Null(l.Url));
    }
}
