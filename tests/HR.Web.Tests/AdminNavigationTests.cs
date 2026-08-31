using HR.Web.Navigation;

namespace HR.Web.Tests;

/// <summary>
/// ADM-07 — <see cref="AdminNavigation"/> is the single permission-scoped source of truth for
/// administrative destinations (sidebar groups + quick-nav palette). These tests pin the
/// visibility filtering and quick-search ranking per persona without bUnit. Persona capability
/// sets mirror the ADM-05 role-separation matrix.
/// </summary>
public class AdminNavigationTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-000000000042");

    private static AdminNavCapabilities Caps(
        bool canReadEmployees = false,
        bool canManageEmployees = false,
        bool canManageRecruitment = false,
        bool canManageLeavePolicies = false,
        bool canManageSharedDocuments = false,
        bool canViewReporting = false,
        bool canViewComplianceCentre = false,
        bool canViewAdminAlerts = false,
        bool canManageCompany = false,
        bool canManageCompanyConfiguration = false,
        bool canManageHrSettings = false,
        bool canViewUsers = false,
        bool isHrAdministrator = false) =>
        new(canReadEmployees, canManageEmployees, canManageRecruitment, canManageLeavePolicies,
            canManageSharedDocuments, canViewReporting, canViewComplianceCentre, canViewAdminAlerts,
            canManageCompany, canManageCompanyConfiguration, canManageHrSettings, canViewUsers,
            isHrAdministrator);

    private static AdminNavCapabilities CompanyAdminAlone() =>
        Caps(canManageCompany: true, canManageCompanyConfiguration: true);

    private static AdminNavCapabilities HrAdmin() =>
        Caps(canManageEmployees: true, canManageLeavePolicies: true, canManageSharedDocuments: true,
            canManageHrSettings: true, canViewReporting: true, canViewComplianceCentre: true,
            canViewAdminAlerts: true, canViewUsers: true, isHrAdministrator: true);

    private static readonly string[] DenyListForCompanyAdmin =
    {
        "Employees", "User Administration", "Leave Policies", "Compliance Centre",
        "Reporting", "Administrative Alerts", "HR Settings",
    };

    [Fact]
    public void HasAnyAdministrativeAccess_FalseForAllFalse()
    {
        Assert.False(Caps().HasAnyAdministrativeAccess);
    }

    [Theory]
    [InlineData("canReadEmployees")]
    [InlineData("canManageEmployees")]
    [InlineData("canManageRecruitment")]
    [InlineData("canManageLeavePolicies")]
    [InlineData("canManageSharedDocuments")]
    [InlineData("canViewReporting")]
    [InlineData("canViewComplianceCentre")]
    [InlineData("canViewAdminAlerts")]
    [InlineData("canManageCompany")]
    [InlineData("canManageCompanyConfiguration")]
    [InlineData("canManageHrSettings")]
    [InlineData("canViewUsers")]
    public void HasAnyAdministrativeAccess_TrueWhenAnySingleFlagSet(string flag)
    {
        var caps = flag switch
        {
            "canReadEmployees" => Caps(canReadEmployees: true),
            "canManageEmployees" => Caps(canManageEmployees: true),
            "canManageRecruitment" => Caps(canManageRecruitment: true),
            "canManageLeavePolicies" => Caps(canManageLeavePolicies: true),
            "canManageSharedDocuments" => Caps(canManageSharedDocuments: true),
            "canViewReporting" => Caps(canViewReporting: true),
            "canViewComplianceCentre" => Caps(canViewComplianceCentre: true),
            "canViewAdminAlerts" => Caps(canViewAdminAlerts: true),
            "canManageCompany" => Caps(canManageCompany: true),
            "canManageCompanyConfiguration" => Caps(canManageCompanyConfiguration: true),
            "canManageHrSettings" => Caps(canManageHrSettings: true),
            "canViewUsers" => Caps(canViewUsers: true),
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };

        Assert.True(caps.HasAnyAdministrativeAccess);
    }

    [Fact]
    public void Build_AllFalseCaps_ReturnsEmpty()
    {
        Assert.Empty(AdminNavigation.Build(Caps(), CompanyId));
    }

    [Fact]
    public void CompanyAdministratorAlone_SeesOnlyCompanyGroupDestinations()
    {
        var destinations = AdminNavigation.Build(CompanyAdminAlone(), CompanyId);

        Assert.NotEmpty(destinations);
        Assert.All(destinations, d => Assert.Equal(AdminNavGroup.Company, d.Group));

        var titles = destinations.Select(d => d.Title).ToList();
        Assert.Contains("Company Profile & Addresses", titles);
        Assert.Contains("Administration Home", titles);
        Assert.Contains("Subscription & Billing", titles);

        // ADM-05 deny list: no HR / people / compliance / reporting / audit surface leaks in.
        foreach (var denied in DenyListForCompanyAdmin)
            Assert.DoesNotContain(denied, titles);
    }

    [Fact]
    public void CompanyAdministratorAlone_Sections_ContainOnlyCompanyGroup()
    {
        var sections = AdminNavigation.Sections(CompanyAdminAlone(), CompanyId);

        var section = Assert.Single(sections);
        Assert.Equal(AdminNavGroup.Company, section.Group);
        Assert.Equal("Company", section.Label);
        Assert.All(section.Destinations, d => Assert.Equal(AdminNavGroup.Company, d.Group));
    }

    [Fact]
    public void HrAdministrator_SeesPeopleHrComplianceReportsAndAuditSections()
    {
        var groups = AdminNavigation.Sections(HrAdmin(), CompanyId).Select(s => s.Group).ToList();

        Assert.Contains(AdminNavGroup.PeopleAndUsers, groups);
        Assert.Contains(AdminNavGroup.HrConfiguration, groups);
        Assert.Contains(AdminNavGroup.Compliance, groups);
        Assert.Contains(AdminNavGroup.Reports, groups);
        Assert.Contains(AdminNavGroup.AuditAndSecurity, groups);
    }

    [Fact]
    public void CanViewUsersAlone_SurfacesExactlyUserAdministration()
    {
        var destinations = AdminNavigation.Build(Caps(canViewUsers: true), CompanyId);

        var only = Assert.Single(destinations);
        Assert.Equal("user-administration", only.Key);
        Assert.Equal("User Administration", only.Title);
        Assert.Equal(AdminNavGroup.PeopleAndUsers, only.Group);
    }

    [Fact]
    public void Sections_AreInEnumOrder_AndLabelsMatchGroupInfo()
    {
        var sections = AdminNavigation.Sections(HrAdmin(), CompanyId);

        var groupOrder = sections.Select(s => (int)s.Group).ToList();
        Assert.Equal(groupOrder.OrderBy(x => x).ToList(), groupOrder);

        Assert.All(sections, s => Assert.Equal(AdminNavGroupInfo.Label(s.Group), s.Label));
    }

    [Fact]
    public void Search_CompanyAdministratorAlone_CannotFindUnreachablePages()
    {
        var caps = CompanyAdminAlone();

        Assert.Empty(AdminNavigation.Search(caps, CompanyId, "employees"));
        Assert.Empty(AdminNavigation.Search(caps, CompanyId, "compliance"));
        Assert.Empty(AdminNavigation.Search(caps, CompanyId, "leave"));
    }

    [Fact]
    public void Search_HrAdministrator_FindsLeaveDestinations()
    {
        var results = AdminNavigation.Search(HrAdmin(), CompanyId, "leave");

        Assert.Contains(results, d => d.Title == "Leave Policies");
        Assert.Contains(results, d => d.Title == "Leave Types");
    }

    [Fact]
    public void Search_RanksExactTitleMatchFirst()
    {
        var results = AdminNavigation.Search(HrAdmin(), CompanyId, "employees");

        Assert.Equal("Employees", results[0].Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_BlankTerm_ReturnsVisibleListCappedAtLimit(string? term)
    {
        var caps = HrAdmin();
        var expected = AdminNavigation.Build(caps, CompanyId).Take(12).Select(d => d.Key).ToList();

        var actual = AdminNavigation.Search(caps, CompanyId, term).Select(d => d.Key).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Search_NeverReturnsMoreThanLimit()
    {
        var caps = HrAdmin();

        Assert.True(AdminNavigation.Search(caps, CompanyId, null, limit: 3).Count <= 3);
        Assert.True(AdminNavigation.Search(caps, CompanyId, "e", limit: 2).Count <= 2);
        Assert.True(AdminNavigation.Search(caps, CompanyId, null).Count <= 12);
    }

    [Fact]
    public void Build_EveryUrlContainsCompanyId_ExceptFixedSubscriptionPath()
    {
        var destinations = AdminNavigation.Build(HrAdmin(), CompanyId);

        Assert.All(destinations, d =>
        {
            if (d.Key == "subscription")
                Assert.Equal("/subscription", d.Url);
            else
                Assert.Contains(CompanyId.ToString(), d.Url);
        });

        Assert.All(destinations, d => Assert.StartsWith("/", d.Url));
    }

    [Fact]
    public void Search_MatchesOnKeywords()
    {
        Assert.Contains(
            AdminNavigation.Search(HrAdmin(), CompanyId, "bank holidays"),
            d => d.Title == "Public Holidays");

        Assert.Contains(
            AdminNavigation.Search(Caps(canViewUsers: true), CompanyId, "permissions"),
            d => d.Title == "User Administration");
    }
}
