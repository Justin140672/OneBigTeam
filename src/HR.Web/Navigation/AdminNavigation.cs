using HR.Web.Services;

namespace HR.Web.Navigation;

/// <summary>
/// ADM-07 — the seven canonical administrative navigation groups. The same ordered set backs
/// both the sidebar's Administration grouping and the permission-aware quick-navigation palette,
/// so a destination can never appear in one and not the other.
/// </summary>
public enum AdminNavGroup
{
    PeopleAndUsers = 0,
    Company = 1,
    HrConfiguration = 2,
    Compliance = 3,
    Reports = 4,
    AuditAndSecurity = 5,
    PlatformOperations = 6,
}

public static class AdminNavGroupInfo
{
    public static string Label(AdminNavGroup group) => group switch
    {
        AdminNavGroup.PeopleAndUsers => "People and users",
        AdminNavGroup.Company => "Company",
        AdminNavGroup.HrConfiguration => "HR configuration",
        AdminNavGroup.Compliance => "Compliance",
        AdminNavGroup.Reports => "Reports",
        AdminNavGroup.AuditAndSecurity => "Audit and security",
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
/// Capability flags the administrative navigation depends on. Kept as a plain value (mirroring
/// <see cref="HR.Web.Components.Pages.Administration.HubCapabilities"/>) so the visibility and
/// quick-search filtering can be unit-tested without bUnit or an <see cref="AppSession"/> round trip.
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
    bool CanViewComplianceCentre,
    bool CanViewAdminAlerts,
    bool CanManageCompany,
    bool CanManageCompanyConfiguration,
    bool CanManageHrSettings,
    bool CanViewUsers,
    bool IsHrAdministrator)
{
    public static AdminNavCapabilities From(AppSession session) => new(
        session.CanReadEmployees,
        session.CanManageEmployees,
        session.CanManageRecruitment,
        session.CanManageLeavePolicies,
        session.CanManageSharedDocuments,
        session.CanViewReporting,
        session.CanViewComplianceCentre,
        session.CanViewAdminAlerts,
        session.CanManageCompany,
        session.CanManageCompanyConfiguration,
        session.CanManageHrSettings,
        session.CanViewUsers,
        session.IsHrAdministrator);

    /// <summary>True when the user can reach at least one administrative destination.</summary>
    public bool HasAnyAdministrativeAccess =>
        CanReadEmployees || CanManageEmployees || CanManageRecruitment || CanManageLeavePolicies ||
        CanManageSharedDocuments || CanViewReporting || CanViewComplianceCentre || CanViewAdminAlerts ||
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
            (caps.CanManageEmployees, new("position-profiles", "Position Profiles", AdminNavGroup.PeopleAndUsers,
                Co("position-profiles"), "fa-solid fa-id-badge", ["jobs", "roles", "titles", "positions"])),
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

            // ---- Company ----
            (caps.CanManageCompanyConfiguration || caps.CanManageCompany, new("company-profile", "Company Profile & Addresses", AdminNavGroup.Company,
                Co("edit"), "fa-solid fa-building", ["company", "legal name", "branding", "addresses", "registered office"])),
            (caps.CanManageHrSettings || caps.CanManageEmployees || caps.CanManageLeavePolicies || caps.CanManageRecruitment
                || caps.CanManageSharedDocuments || caps.CanManageCompany || caps.CanManageCompanyConfiguration,
                new("administration-hub", "Administration Home", AdminNavGroup.Company,
                Co("administration"), "fa-solid fa-gears", ["admin", "settings", "configuration", "hub"])),
            (caps.CanManageCompany || caps.IsHrAdministrator, new("subscription", "Subscription & Billing", AdminNavGroup.Company,
                "/subscription", "fa-solid fa-credit-card", ["plan", "invoices", "payment", "billing"])),
            (caps.IsHrAdministrator || caps.CanManageCompany, new("support-queue", "Support Requests", AdminNavGroup.Company,
                Co("support/admin/queue"), "fa-solid fa-headset", ["tickets", "help", "feedback", "support queue"])),

            // ---- HR configuration ----
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

            // ---- Compliance ----
            (caps.CanViewComplianceCentre, new("compliance-centre", "Compliance Centre", AdminNavGroup.Compliance,
                Co("reporting/compliance-centre"), "fa-solid fa-shield-halved",
                ["expiring visas", "certifications", "missing documents", "probation reviews due", "right to work"])),

            // ---- Reports ----
            (caps.CanViewReporting, new("reporting", "Reporting", AdminNavGroup.Reports,
                Co("reporting"), "fa-solid fa-chart-column", ["reports", "analytics", "saved views", "exports"])),

            // ---- Audit and security ----
            (caps.CanViewAdminAlerts, new("administrative-alerts", "Administrative Alerts", AdminNavGroup.AuditAndSecurity,
                Co("administrative-alerts"), "fa-solid fa-triangle-exclamation",
                ["incidents", "security alerts", "failed reports", "failed integrations", "alerts inbox"])),
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

    /// <summary>
    /// Quick-navigation search. Filters <see cref="Build"/> — which is already permission-scoped —
    /// so a destination the user cannot reach can never appear in the results (ADM-07). A
    /// blank/whitespace term returns every visible destination (the palette's initial listing).
    /// Matches on title, group label and keywords, case-insensitively; a title match ranks first,
    /// then a title prefix, then keyword/group matches, preserving group order within each rank.
    /// </summary>
    public static IReadOnlyList<AdminDestination> Search(
        AdminNavCapabilities caps, Guid companyId, string? term, int limit = 12)
    {
        var visible = Build(caps, companyId);
        if (string.IsNullOrWhiteSpace(term))
            return visible.Take(limit).ToList();

        var t = term.Trim();

        int Rank(AdminDestination d)
        {
            if (d.Title.Equals(t, StringComparison.OrdinalIgnoreCase)) return 0;
            if (d.Title.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return 1;
            if (d.Title.Contains(t, StringComparison.OrdinalIgnoreCase)) return 2;
            if (AdminNavGroupInfo.Label(d.Group).Contains(t, StringComparison.OrdinalIgnoreCase)) return 3;
            if (d.Keywords.Any(k => k.Contains(t, StringComparison.OrdinalIgnoreCase))) return 4;
            return int.MaxValue;
        }

        return visible
            .Select(d => (Destination: d, Rank: Rank(d)))
            .Where(x => x.Rank != int.MaxValue)
            .OrderBy(x => x.Rank)
            .ThenBy(x => (int)x.Destination.Group)
            .Select(x => x.Destination)
            .Take(limit)
            .ToList();
    }
}
