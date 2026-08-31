using HR.Web.Services;

namespace HR.Web.Components.Pages.Administration;

/// <summary>A single link inside an Administration-hub category card. A null <see cref="Url"/>
/// renders as a "Not yet configurable" marker rather than an anchor.</summary>
public sealed record HubLink(string Text, string? Url);

/// <summary>A visible Administration-hub category card.</summary>
public sealed record HubCategory(string Title, string Explanation, string Requires, IReadOnlyList<HubLink> Links);

/// <summary>The capability flags the Administration hub's per-category visibility depends on.
/// Kept as a plain value so the visibility mapping can be unit-tested without bUnit or an
/// <see cref="AppSession"/> HTTP round trip.</summary>
public sealed record HubCapabilities(
    bool CanManageCompany,
    bool CanManageCompanyConfiguration,
    bool CanManageEmployees,
    bool CanManageLeavePolicies,
    bool CanManageRecruitment,
    bool CanManageSharedDocuments,
    bool CanManageHrSettings,
    bool IsHrAdministrator)
{
    public static HubCapabilities From(AppSession session) => new(
        session.CanManageCompany,
        session.CanManageCompanyConfiguration,
        session.CanManageEmployees,
        session.CanManageLeavePolicies,
        session.CanManageRecruitment,
        session.CanManageSharedDocuments,
        session.CanManageHrSettings,
        session.IsHrAdministrator);
}

/// <summary>Pure mapping from capability flags to the ordered list of Administration-hub
/// categories the user may see (ADM-04). No rendering concerns live here.</summary>
public static class AdministrationHubCategories
{
    /// <summary>The ordered set of categories visible for the given capabilities, with their links.</summary>
    public static IReadOnlyList<HubCategory> Build(HubCapabilities caps, Guid companyId)
    {
        var c = companyId;
        var result = new List<HubCategory>();

        var companyProfileVisible = caps.CanManageCompanyConfiguration || caps.CanManageCompany;

        if (companyProfileVisible)
            result.Add(new HubCategory(
                "Company profile and addresses",
                "The company's legal name, trading details and registered addresses.",
                "company:manage",
                [new HubLink("Company profile & addresses", $"/companies/{c}/edit")]));

        if (companyProfileVisible)
            result.Add(new HubCategory(
                "Company defaults",
                "Default timezone, locale, numbering and formatting rules.",
                "company:manage",
                [
                    new HubLink("Timezone, locale & formatting defaults", null),
                    new HubLink("Employee & asset numbering",
                        caps.CanManageHrSettings ? $"/companies/{c}/hr-settings" : null),
                ]));

        if (caps.CanManageEmployees || caps.CanManageLeavePolicies)
            result.Add(new HubCategory(
                "Leave",
                "Leave types, policies, public holidays and leave-year defaults.",
                "employee:manage / leave policies",
                Compact(
                    caps.CanManageEmployees ? new HubLink("Leave Types", $"/companies/{c}/leave-types") : null,
                    caps.CanManageLeavePolicies ? new HubLink("Leave Policies", $"/companies/{c}/leave-policies") : null,
                    caps.CanManageEmployees ? new HubLink("Public Holidays", $"/companies/{c}/public-holidays") : null,
                    caps.CanManageHrSettings ? new HubLink("Leave year & allowance defaults", $"/companies/{c}/hr-settings") : null)));

        if (caps.CanManageRecruitment)
            result.Add(new HubCategory(
                "Recruitment",
                "Recruitment pipeline stages, external recruiters and recruitment settings.",
                "recruitment:manage",
                [
                    new HubLink("Recruitment Stages", $"/companies/{c}/recruitment-stages"),
                    new HubLink("External Recruiters", $"/companies/{c}/external-recruiters"),
                    new HubLink("Recruitment settings", null),
                ]));

        if (caps.CanManageEmployees || caps.CanManageSharedDocuments)
            result.Add(new HubCategory(
                "Documents",
                "Document types, shared company documents and reminder defaults.",
                "employee:manage / shared-document:manage",
                Compact(
                    caps.CanManageEmployees ? new HubLink("Document Types", $"/companies/{c}/document-types") : null,
                    caps.CanManageSharedDocuments ? new HubLink("Shared Documents", $"/companies/{c}/shared-documents") : null,
                    caps.CanManageHrSettings ? new HubLink("Document acknowledgement & reminder defaults", $"/companies/{c}/hr-settings") : null)));

        if (caps.CanManageHrSettings)
            result.Add(new HubCategory(
                "Notifications",
                "Company-wide notification and reminder preferences.",
                "hr-settings:manage",
                [new HubLink("Notification settings", null)]));

        if (caps.CanManageHrSettings)
            result.Add(new HubCategory(
                "Probation",
                "Default probation period and probation review defaults.",
                "hr-settings:manage",
                [
                    new HubLink("Probation period defaults", $"/companies/{c}/hr-settings"),
                    new HubLink("Dedicated probation settings", null),
                ]));

        if (caps.CanManageCompany || caps.IsHrAdministrator)
            result.Add(new HubCategory(
                "Subscription",
                "The company's subscription plan and billing details.",
                "subscription:manage",
                [new HubLink("Subscription & billing", "/subscription")]));

        return result;
    }

    /// <summary>Just the ordered titles of the visible categories — the primary unit-test surface.</summary>
    public static IReadOnlyList<string> VisibleTitles(HubCapabilities caps) =>
        Build(caps, Guid.Empty).Select(x => x.Title).ToList();

    private static IReadOnlyList<HubLink> Compact(params HubLink?[] links) =>
        links.Where(l => l is not null).Select(l => l!).ToList();
}
