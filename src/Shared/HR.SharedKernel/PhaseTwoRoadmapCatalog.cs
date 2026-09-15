namespace HR.SharedKernel;

// Shared by the database seed and the marketing site's offline fallback.
// Ease and customer value are planning scores out of five, not public marketing copy.
public sealed record PhaseTwoRoadmapItem(Guid Id, string IconName, string Title, string Description,
    int Ease, int CustomerValue, string? LegacyTitle = null);

public static class PhaseTwoRoadmapCatalog
{
    public static IReadOnlyList<PhaseTwoRoadmapItem> All { get; } = Array.AsReadOnly(new PhaseTwoRoadmapItem[]
    {
        new(new Guid("c824eda0-862c-4602-b683-000000000001"), "clipboard-check",
            "Performance Reviews & Appraisals",
            "Run structured employee appraisals and performance reviews, with manager and employee input and a complete review history.", 3, 5, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000002"), "diagram-project",
            "Webhooks & Integrations",
            "Connect One Big Team with payroll, timesheets and other business systems through webhooks and integrations.", 3, 4, "Employee webhooks"),
        new(new Guid("c824eda0-862c-4602-b683-000000000003"), "graduation-cap",
            "Training & Development",
            "Record employee training, track completion and maintain a clear history of learning and development.", 3, 5, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000004"), "certificate",
            "Qualifications & Certification Compliance",
            "Track required qualifications and certifications, evidence and expiry dates, with reminders when action is needed.", 3, 5, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000005"), "list-check",
            "Configurable offboarding templates",
            "Configure reusable offboarding checklists, assign responsibilities and set deadlines for employee departures.", 3, 5, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000006"), "comments",
            "Employee Engagement & Surveys",
            "Run employee surveys and quick pulse checks, collect anonymous feedback and understand engagement trends over time.", 3, 4, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000007"), "arrow-up-right-from-square",
            "External Tools & Employee Quick Links",
            "Give employees quick access to timesheets, expenses, payslips, benefits and other external systems directly from My Profile.", 4, 4, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000008"), "laptop",
            "IT Equipment Handover & Recovery",
            "Record equipment handovers, employee acknowledgement and the return of company equipment during offboarding.", 4, 4, null),
        new(new Guid("c824eda0-862c-4602-b683-000000000009"), "wand-magic-sparkles",
            "AI-generated Live Job Descriptions",
            "Create and maintain job descriptions from Position Profiles, keeping them aligned with the role as it evolves.", 4, 4, "AI-powered position profiles"),
        new(new Guid("c824eda0-862c-4602-b683-000000000010"), "robot",
            "AI Help Assistant",
            "Help users find answers and understand how to use One Big Team through an integrated AI assistant.", 3, 4, "AI help assistant"),
    });
}

