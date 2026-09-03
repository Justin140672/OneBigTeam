namespace HR.Web.Components.Pages.Employees;

/// <summary>
/// Top-level navigation groups for the employee profile screen. Enum order here also drives
/// the order of the outer tab strip.
/// </summary>
public enum EmployeeProfileGroup
{
    Overview,
    CareerPay,
    TimeOff,
    TasksRecords,
    Assets,
    Activity
}

/// <summary>
/// Individual sub-sections of the employee profile screen. These are the stable keys that
/// replace the old hand-computed positional tab indices — nothing here is positional, and the
/// integer values are only persisted to localStorage (see <c>EmployeeEdit.OnAfterRenderAsync</c>),
/// so do not reorder existing members without accepting that stored "last section" values reset.
/// </summary>
public enum EmployeeProfileSection
{
    Details,
    Employment,
    Probation,
    EmergencyContacts,
    Compensation,
    Promotions,
    Leave,
    Sickness,
    Tasks,
    Documents,
    Acknowledgements,
    Onboarding,
    Offboarding,
    Leaving,
    Assets,
    Timeline,
    Notes,
    Audit
}

public sealed record EmployeeProfileSectionDef(
    EmployeeProfileGroup Group,
    EmployeeProfileSection Section,
    string Key,
    string Label);

public static class EmployeeProfileNavigation
{
    public static string GroupLabel(EmployeeProfileGroup group) => group switch
    {
        EmployeeProfileGroup.Overview => "Overview",
        EmployeeProfileGroup.CareerPay => "Career & Pay",
        EmployeeProfileGroup.TimeOff => "Time Off",
        EmployeeProfileGroup.TasksRecords => "Tasks & Records",
        EmployeeProfileGroup.Assets => "Assets",
        EmployeeProfileGroup.Activity => "Activity",
        _ => group.ToString()
    };

    /// <summary>Canonical display order of every section, grouped.</summary>
    public static readonly IReadOnlyList<EmployeeProfileSectionDef> All = new List<EmployeeProfileSectionDef>
    {
        new(EmployeeProfileGroup.Overview, EmployeeProfileSection.Details, "details", "Details"),
        new(EmployeeProfileGroup.Overview, EmployeeProfileSection.Employment, "employment", "Employment"),
        new(EmployeeProfileGroup.Overview, EmployeeProfileSection.Probation, "probation", "Probation"),
        new(EmployeeProfileGroup.Overview, EmployeeProfileSection.EmergencyContacts, "emergency-contacts", "Emergency Contacts"),
        new(EmployeeProfileGroup.CareerPay, EmployeeProfileSection.Compensation, "compensation", "Compensation History"),
        new(EmployeeProfileGroup.CareerPay, EmployeeProfileSection.Promotions, "promotions", "Promotion History"),
        new(EmployeeProfileGroup.TimeOff, EmployeeProfileSection.Leave, "leave", "Leave"),
        new(EmployeeProfileGroup.TimeOff, EmployeeProfileSection.Sickness, "sickness", "Sickness"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Tasks, "tasks", "Tasks"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Documents, "documents", "Documents"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Acknowledgements, "acknowledgements", "Acknowledgement History"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Onboarding, "onboarding", "Onboarding"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Offboarding, "offboarding", "Offboarding"),
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Leaving, "leaving", "Leaving"),
        new(EmployeeProfileGroup.Assets, EmployeeProfileSection.Assets, "assets", "Assets"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Timeline, "timeline", "Timeline"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Notes, "notes", "Notes"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Audit, "audit", "Audit"),
    };

    /// <summary>Order of the outer group strip.</summary>
    public static readonly IReadOnlyList<EmployeeProfileGroup> GroupOrder = new[]
    {
        EmployeeProfileGroup.Overview,
        EmployeeProfileGroup.CareerPay,
        EmployeeProfileGroup.TimeOff,
        EmployeeProfileGroup.TasksRecords,
        EmployeeProfileGroup.Assets,
        EmployeeProfileGroup.Activity
    };

    public static EmployeeProfileSectionDef Def(EmployeeProfileSection section) =>
        All.First(d => d.Section == section);

    public static EmployeeProfileGroup GroupOf(EmployeeProfileSection section) => Def(section).Group;

    /// <summary>
    /// Maps a legacy or current <c>?tab=</c> query value onto a section. Every value the old
    /// positional switch understood (<c>probation, leave, sickness, documents, onboarding,
    /// offboarding, leaving, timeline</c>) is still accepted, and because the new scheme keys off
    /// the per-section <see cref="EmployeeProfileSectionDef.Key"/> the caller can now also deep-link
    /// to any other section (e.g. <c>?tab=compensation</c> -&gt; Career &amp; Pay &gt; Compensation).
    /// Returns <c>null</c> for an unrecognised value so the caller falls back to the default section.
    /// </summary>
    public static EmployeeProfileSection? ParseTab(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim().ToLowerInvariant();

        var match = All.FirstOrDefault(d => d.Key == v);
        if (match is not null)
            return match.Section;

        return v switch
        {
            "acknowledgement" or "acknowledgements-history" => EmployeeProfileSection.Acknowledgements,
            "compensation-history" => EmployeeProfileSection.Compensation,
            "promotion" or "promotion-history" => EmployeeProfileSection.Promotions,
            "emergency" or "emergency-contact" => EmployeeProfileSection.EmergencyContacts,
            _ => null
        };
    }
}
