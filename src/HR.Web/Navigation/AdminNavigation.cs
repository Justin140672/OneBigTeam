using HR.Web.Services;

namespace HR.Web.Navigation;

/// <summary>
/// ADM-07 — the canonical administrative navigation groups. The same ordered set backs the
/// sidebar's Administration grouping.
///
/// ADM (nav simplification) — the generic "Compliance" group was removed (its only destination,
/// the Compliance Centre, is no longer in navigation; the page stays reachable by direct URL and
/// remains API-protected). The generic "Company" group was replaced by
/// <see cref="CompanyAdministration"/>, a role-specific section only a Company Administrator
/// populates (Company Profile, Subscription &amp; Billing) — there is no longer a generic
/// company/administration landing bucket, and the Administration Home hub is no longer in nav.
/// </summary>
public enum AdminNavGroup
{
    PeopleAndUsers = 0,
    CompanyAdministration = 1,
    HrConfiguration = 2,
    Reports = 4,
    PlatformOperations = 6,
}

public static class AdminNavGroupInfo
{
    public static string Label(AdminNavGroup group) => group switch
    {
        AdminNavGroup.PeopleAndUsers => "People and users",
        AdminNavGroup.CompanyAdministration => "Company administration",
        AdminNavGroup.HrConfiguration => "HR configuration",
        AdminNavGroup.Reports => "Reports",
        AdminNavGroup.PlatformOperations => "Platform operations",
        _ => group.ToString(),
    };
}

/// <summary>A single administrative destination the user can navigate to.</summary>
public sealed record AdminDestination(
    string Key,
    string Title,
    AdminNavGroup Group,
    string Url,
    string IconCss,
    IReadOnlyList<string> Keywords);

/// <summary>A group header plus the destinations visible within it, in display order.</summary>
public sealed record AdminNavSection(AdminNavGroup Group, string Label, IReadOnlyList<AdminDestination> Destinations);

/// <summary>
/// Capability flags the administrative navigation depends on. Kept as a plain value so the
/// visibility filtering can be unit-tested without bUnit or an <see cref="AppSession"/> round trip.
/// Every flag is permission-derived on <see cref="AppSession"/> — the authoritative UI gate per the
/// ADM-05 administrative role separation matrix. UI hiding is a usability layer only; the API is the
/// enforcement boundary.
/// </summary>
public sealed record AdminNavCapabilities(
    bool CanReadEmployees,
    bool CanManageEmployees,
    bool CanManageRecruitment,
    bool CanManageLeavePolicies,
    bool CanManageSharedDocuments,
    bool CanViewReporting,
    bool CanManageCompany,
    bool CanManageCompanyConfiguration,
    bool CanManageHrSettings,
    bool CanViewUsers)
{
    public static AdminNavCapabilities From(AppSession session) => new(
        session.CanReadEmployees,
        session.CanManageEmployees,
        session.CanManageRecruitment,
        session.CanManageLeavePolicies,
        session.CanManageSharedDocuments,
        session.CanViewReporting,
        session.CanManageCompany,
        session.CanManageCompanyConfiguration,
        session.CanManageHrSettings,
        session.CanViewUsers);

    /// <summary>True when the user can reach at least one administrative destination.</summary>
    public bool HasAnyAdministrativeAccess =>
        CanReadEmployees || CanManageEmployees || CanManageRecruitment || CanManageLeavePolicies ||
        CanManageSharedDocuments || CanViewReporting ||
        CanManageCompany || CanManageCompanyConfiguration || CanManageHrSettings || CanViewUsers;
}

/// <summary>
/// ADM-07 — the single source of truth for administrative destinations in the tenant app. Pure:
/// no rendering, no DI. <see cref="Build"/> returns only the destinations the given capabilities
/// may reach, so anything derived from it (the sidebar Administration groups, the quick-nav
/// palette) inherits the permission filtering for free and can never surface a page the user
/// cannot open.
/// </summary>
public static class AdminNavigation
{
    /// <summary>Every destination the given capabilities can reach, ordered by group then declaration order.</summary>
    public static IReadOnlyList<AdminDestination> Build(AdminNavCapabilities caps, Guid companyId)
    {
        var c = companyId;
        string Co(string suffix) => $"/companies/{c}/{suffix}";

        var all = new List<(bool Visible, AdminDestination Destination)>
        {
            // ---- People and users ----
            (caps.CanReadEmployees || caps.CanManageEmployees, new("employees", "Employees", AdminNavGroup.PeopleAndUsers,
                Co("employees"), "fa-solid fa-users", ["people", "staff", "directory", "colleagues", "team"])),
            (caps.CanManageEmployees, new("departments", "Departments", AdminNavGroup.PeopleAndUsers,
                Co("departments"), "fa-solid fa-sitemap", ["teams", "org", "structure", "division"])),
            (caps.CanManageEmployees, new("locations", "Locations", AdminNavGroup.PeopleAndUsers,
                Co("locations"), "fa-solid fa-location-dot", ["sites", "offices", "workplaces"])),
            (caps.CanManageEmployees, new("organisation-chart", "Organisation Chart", AdminNavGroup.PeopleAndUsers,
                Co("organisation-chart"), "fa-solid fa-diagram-project", ["org chart", "hierarchy", "reporting line"])),
            (caps.CanManageEmployees, new("employment-types", "Employment Types", AdminNavGroup.PeopleAndUsers,
                Co("employment-types"), "fa-solid fa-file-signature", ["permanent", "contractor", "fixed term"])),
            (caps.CanManageEmployees, new("location-types", "Location Types", AdminNavGroup.PeopleAndUsers,
                Co("location-types"), "fa-solid fa-map", ["remote", "hybrid", "on site"])),
            (caps.CanManageEmployees, new("data-import", "Data Import", AdminNavGroup.PeopleAndUsers,
                Co("data-import/employees"), "fa-solid fa-file-import", ["bulk", "upload", "onboarding import", "migrate"])),
            (caps.CanViewUsers, new("user-administration", "User Administration", AdminNavGroup.PeopleAndUsers,
                Co("user-administration"), "fa-solid fa-user-shield", ["users", "roles", "permissions", "invites", "access", "overrides"])),
            (caps.CanManageEmployees, new("assets", "Assets", AdminNavGroup.PeopleAndUsers,
                Co("assets"), "fa-solid fa-box", ["equipment", "devices", "hardware"])),
            (caps.CanManageEmployees, new("asset-categories", "Asset Categories", AdminNavGroup.PeopleAndUsers,
                Co("asset-categories"), "fa-solid fa-boxes-stacked", ["equipment groups", "asset types"])),

            // ---- Company administration (Company Administrator only) ----
            // ADM (nav simplification): the generic "Company" bucket, its Administration Home hub
            // link and the duplicate Support Requests entry (still rendered by MainLayout's own
            // dedicated block) were removed. Company Profile stays for whoever holds company:manage;
            // Subscription & Billing is Company-Administrator-only (see 30-administrative-role-
            // separation-matrix.md) — CanManageCompany is true iff the user holds the
            // CompanyAdministrator role, and the API enforces the same via subscription:manage.
            (caps.CanManageCompanyConfiguration || caps.CanManageCompany, new("company-profile", "Company Profile & Addresses", AdminNavGroup.CompanyAdministration,
                Co("edit"), "fa-solid fa-building", ["company", "legal name", "branding", "addresses", "registered office"])),
            (caps.CanManageCompany, new("subscription", "Subscription & Billing", AdminNavGroup.CompanyAdministration,
                "/subscription", "fa-solid fa-credit-card", ["plan", "invoices", "payment", "billing"])),

            // ---- HR configuration ----
            // Position Profiles lives here (not People and users): it is job-role configuration —
            // titles, permission sets, required documents/assets, onboarding template and notice
            // defaults — consumed when setting an employee up, not a people directory screen. The
            // breadcrumb on the Position Profile pages says "HR configuration" to match.
            (caps.CanManageEmployees, new("position-profiles", "Position Profiles", AdminNavGroup.HrConfiguration,
                Co("position-profiles"), "fa-solid fa-id-badge", ["jobs", "roles", "titles", "positions"])),
            (caps.CanManageHrSettings, new("hr-settings", "HR Settings", AdminNavGroup.HrConfiguration,
                Co("hr-settings"), "fa-solid fa-sliders", ["leave year", "probation", "salary display", "reminders", "numbering"])),
            (caps.CanManageEmployees, new("leave-types", "Leave Types", AdminNavGroup.HrConfiguration,
                Co("leave-types"), "fa-solid fa-calendar-day", ["annual leave", "holiday", "sick leave", "absence types"])),
            (caps.CanManageLeavePolicies, new("leave-policies", "Leave Policies", AdminNavGroup.HrConfiguration,
                Co("leave-policies"), "fa-solid fa-calendar-check", ["entitlement", "carry over", "allowance rules"])),
            (caps.CanManageEmployees, new("public-holidays", "Public Holidays", AdminNavGroup.HrConfiguration,
                Co("public-holidays"), "fa-solid fa-umbrella-beach", ["bank holidays", "statutory days"])),
            (caps.CanManageEmployees, new("sickness-categories", "Sickness Categories", AdminNavGroup.HrConfiguration,
                Co("sickness-categories"), "fa-solid fa-notes-medical", ["illness", "absence reason", "sick reasons"])),
            (caps.CanManageEmployees, new("document-types", "Document Types", AdminNavGroup.HrConfiguration,
                Co("document-types"), "fa-solid fa-file-lines", ["document categories", "required documents"])),
            (caps.CanManageSharedDocuments, new("shared-documents", "Shared Documents", AdminNavGroup.HrConfiguration,
                Co("shared-documents"), "fa-solid fa-folder-open", ["company documents", "policies", "handbook"])),
            (caps.CanManageEmployees, new("onboarding-templates", "Onboarding Templates", AdminNavGroup.HrConfiguration,
                Co("onboarding-templates"), "fa-solid fa-list-check", ["new starter", "checklist", "induction"])),
            (caps.CanManageRecruitment, new("recruitment-stages", "Recruitment Stages", AdminNavGroup.HrConfiguration,
                Co("recruitment-stages"), "fa-solid fa-diagram-next", ["pipeline", "hiring stages", "ats"])),
            (caps.CanManageRecruitment, new("external-recruiters", "External Recruiters", AdminNavGroup.HrConfiguration,
                Co("external-recruiters"), "fa-solid fa-people-arrows", ["agencies", "recruitment partners"])),

            // ---- Reports ----
            (caps.CanViewReporting, new("reporting", "Reporting", AdminNavGroup.Reports,
                Co("reporting"), "fa-solid fa-chart-column", ["reports", "analytics", "saved views", "exports"])),
        };

        return all.Where(x => x.Visible).Select(x => x.Destination)
            .OrderBy(d => (int)d.Group)
            .ToList();
    }

    /// <summary>The visible destinations grouped into ordered sections — the sidebar Administration surface.</summary>
    public static IReadOnlyList<AdminNavSection> Sections(AdminNavCapabilities caps, Guid companyId) =>
        Build(caps, companyId)
            .GroupBy(d => d.Group)
            .OrderBy(g => (int)g.Key)
            .Select(g => new AdminNavSection(g.Key, AdminNavGroupInfo.Label(g.Key), g.ToList()))
            .ToList();
}
