namespace HR.Web.Components.Pages.Employees;

public enum EmployeeProfileGroup
{
    Overview,
    CareerPay,
    TimeOff,
    TasksRecords,
    Assets,
    Activity
}

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
        new(EmployeeProfileGroup.TasksRecords, EmployeeProfileSection.Leaving, "leaving", "Leaving & Offboarding"),
        new(EmployeeProfileGroup.Assets, EmployeeProfileSection.Assets, "assets", "Assets"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Timeline, "timeline", "Timeline"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Notes, "notes", "Notes"),
        new(EmployeeProfileGroup.Activity, EmployeeProfileSection.Audit, "audit", "Audit"),
    };

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
            "offboarding" => EmployeeProfileSection.Leaving,
            _ => null
        };
    }
}
