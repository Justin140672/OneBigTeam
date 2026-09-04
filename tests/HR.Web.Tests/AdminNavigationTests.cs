using HR.Web.Navigation;

namespace HR.Web.Tests;

/// <summary>
/// ADM-07 / ADM (nav simplification) — <see cref="AdminNavigation"/> is the single
/// permission-scoped source of truth for administrative sidebar destinations. These tests pin the
/// visibility filtering per persona without bUnit. Persona capability sets mirror the ADM-05
/// role-separation matrix (specifications/product-specifications/30-administrative-role-separation-matrix.md).
///
/// Post-simplification expectations:
///  - No "Compliance Centre" destination for any persona (page stays reachable by direct URL only).
///  - No "Administration Home" hub destination for any persona.
///  - No generic "Company" group — Company-Administrator items live in "Company administration".
///  - "Subscription &amp; Billing" is Company-Administrator-only (CanManageCompany); an HR-Admin-only
///    persona never sees it; a combined HR + Company Admin persona does.
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
        bool canManageCompany = false,
        bool canManageCompanyConfiguration = false,
        bool canManageHrSettings = false,
        bool canViewUsers = false) =>
        new(canReadEmployees, canManageEmployees, canManageRecruitment, canManageLeavePolicies,
            canManageSharedDocuments, canViewReporting,
            canManageCompany, canManageCompanyConfiguration, canManageHrSettings, canViewUsers);

    private static AdminNavCapabilities CompanyAdminAlone() =>
        Caps(canManageCompany: true, canManageCompanyConfiguration: true);

    private static AdminNavCapabilities HrAdmin() =>
        Caps(canManageEmployees: true, canManageLeavePolicies: true, canManageSharedDocuments: true,
            canManageHrSettings: true, canViewReporting: true, canViewUsers: true);

    private static AdminNavCapabilities HrPlusCompanyAdmin() =>
        Caps(canManageEmployees: true, canManageLeavePolicies: true, canManageSharedDocuments: true,
            canManageHrSettings: true, canViewReporting: true, canViewUsers: true,
            canManageCompany: true, canManageCompanyConfiguration: true);

    private static readonly string[] DenyListForCompanyAdmin =
    {
        "Employees", "User Administration", "Leave Policies", "Compliance Centre",
        "Reporting", "HR Settings", "Administration Home", "Support Requests",
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
    public void NoPersona_EverSees_ComplianceCentre_Or_AdministrationHome()
    {
        foreach (var caps in new[] { CompanyAdminAlone(), HrAdmin(), HrPlusCompanyAdmin() })
        {
            var titles = AdminNavigation.Build(caps, CompanyId).Select(d => d.Title).ToList();
            Assert.DoesNotContain("Compliance Centre", titles);
            Assert.DoesNotContain("Administration Home", titles);
        }
    }

    [Fact]
    public void NoGroup_IsLabelled_Company_Or_Compliance()
    {
        var labels = AdminNavigation.Sections(HrPlusCompanyAdmin(), CompanyId).Select(s => s.Label).ToList();
        Assert.DoesNotContain("Company", labels);
        Assert.DoesNotContain("Compliance", labels);
    }

    [Fact]
    public void CompanyAdministratorAlone_SeesOnlyCompanyAdministrationDestinations()
    {
        var destinations = AdminNavigation.Build(CompanyAdminAlone(), CompanyId);

        Assert.NotEmpty(destinations);
        Assert.All(destinations, d => Assert.Equal(AdminNavGroup.CompanyAdministration, d.Group));

        var titles = destinations.Select(d => d.Title).ToList();
        Assert.Contains("Company Profile & Addresses", titles);
        Assert.Contains("Subscription & Billing", titles);

        foreach (var denied in DenyListForCompanyAdmin)
            Assert.DoesNotContain(denied, titles);
    }

    [Fact]
    public void CompanyAdministratorAlone_Sections_ContainOnlyCompanyAdministrationGroup()
    {
        var sections = AdminNavigation.Sections(CompanyAdminAlone(), CompanyId);

        var section = Assert.Single(sections);
        Assert.Equal(AdminNavGroup.CompanyAdministration, section.Group);
        Assert.Equal("Company administration", section.Label);
    }

    [Fact]
    public void Subscription_IsCompanyAdministratorOnly()
    {
        Assert.Contains(AdminNavigation.Build(CompanyAdminAlone(), CompanyId), d => d.Key == "subscription");
        Assert.DoesNotContain(AdminNavigation.Build(HrAdmin(), CompanyId), d => d.Key == "subscription");
        Assert.Contains(AdminNavigation.Build(HrPlusCompanyAdmin(), CompanyId), d => d.Key == "subscription");
    }

    [Fact]
    public void HrAdministrator_SeesPeopleHrAndReportsSections_ButNotComplianceOrCompany()
    {
        var groups = AdminNavigation.Sections(HrAdmin(), CompanyId).Select(s => s.Group).ToList();

        Assert.Contains(AdminNavGroup.PeopleAndUsers, groups);
        Assert.Contains(AdminNavGroup.HrConfiguration, groups);
        Assert.Contains(AdminNavGroup.Reports, groups);
        Assert.DoesNotContain(AdminNavGroup.CompanyAdministration, groups);
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
        var sections = AdminNavigation.Sections(HrPlusCompanyAdmin(), CompanyId);

        var groupOrder = sections.Select(s => (int)s.Group).ToList();
        Assert.Equal(groupOrder.OrderBy(x => x).ToList(), groupOrder);

        Assert.All(sections, s => Assert.Equal(AdminNavGroupInfo.Label(s.Group), s.Label));
    }

    // IAM-08 — a Company-Administrator-only capability set (company.read/edit, onboarding,
    // subscription, support; NO employee.read / employee.edit) must never surface any employee-,
    // leave-, sickness-, recruitment-, HR-reporting- or HR-settings-oriented destination.
    [Fact]
    public void CompanyAdministratorOnly_Nav_HasNoEmployeeOrHrDestinations()
    {
        var destinations = AdminNavigation.Build(CompanyAdminAlone(), CompanyId);
        var keys = destinations.Select(d => d.Key).ToList();

        Assert.DoesNotContain("employees", keys);
        Assert.DoesNotContain("departments", keys);
        Assert.DoesNotContain("leave-types", keys);
        Assert.DoesNotContain("leave-policies", keys);
        Assert.DoesNotContain("sickness-categories", keys);
        Assert.DoesNotContain("recruitment-stages", keys);
        Assert.DoesNotContain("external-recruiters", keys);
        Assert.DoesNotContain("reporting", keys);
        Assert.DoesNotContain("hr-settings", keys);
        Assert.DoesNotContain("user-administration", keys);

        // Only company-administration destinations remain.
        Assert.All(destinations, d => Assert.Equal(AdminNavGroup.CompanyAdministration, d.Group));
    }

    // IAM-08 — once employee.read is granted (Company Administrator + HR Administrator) the
    // Employees destination appears.
    [Fact]
    public void CompanyAdministratorPlusHrAdministrator_Nav_HasEmployeesDestination()
    {
        Assert.Contains(AdminNavigation.Build(HrPlusCompanyAdmin(), CompanyId), d => d.Key == "employees");
        Assert.Contains(AdminNavigation.Build(Caps(canReadEmployees: true), CompanyId), d => d.Key == "employees");
    }

    [Fact]
    public void Build_EveryUrlContainsCompanyId_ExceptFixedSubscriptionPath()
    {
        var destinations = AdminNavigation.Build(HrPlusCompanyAdmin(), CompanyId);

        Assert.All(destinations, d =>
        {
            if (d.Key == "subscription")
                Assert.Equal("/subscription", d.Url);
            else
                Assert.Contains(CompanyId.ToString(), d.Url);
        });

        Assert.All(destinations, d => Assert.StartsWith("/", d.Url));
    }
}
