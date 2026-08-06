namespace HR.Marketing.Services;

public sealed record FeatureInfo(
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    IReadOnlyList<string> Benefits,
    string? YouTubeId = null);

public static class FeatureCatalog
{
    public static readonly IReadOnlyList<FeatureInfo> All = new[]
    {
        new FeatureInfo(
            "employee-management",
            "users",
            "Employee Management",
            "Store employee information, documents and employment history in one secure place.",
            "Keep core employee information organised, accessible and consistent as your business grows beyond ad hoc files and scattered records.",
            new[]
            {
                "Employee records and employment history",
                "Departments, locations and position profiles",
                "Documents and document requests",
                "Compensation and employment changes",
                "Notes and audit history",
                "Employee self-service profile"
            }),
        new FeatureInfo(
            "leave-management",
            "calendar-days",
            "Leave Management",
            "Request, approve and track annual leave without spreadsheets or email.",
            "Bring leave requests, balances and approvals into a shared process so everyone understands who is away and what needs action.",
            new[]
            {
                "Annual leave requests and approvals",
                "Configurable leave types and allowances",
                "Team calendars and leave visibility",
                "Leave balances and adjustments",
                "Public holiday support",
                "Approval workflows and reminders"
            }),
        new FeatureInfo(
            "sickness-absence",
            "heart-pulse",
            "Sickness & Absence",
            "Record sickness, return-to-work meetings and absence trends.",
            "Track sickness and other absence clearly, so managers can respond with better context and keep records complete without relying on inbox history.",
            new[]
            {
                "Record sickness and absence events",
                "Return-to-work meetings and notes",
                "Fit notes and supporting documents",
                "Bradford Factor and absence trends",
                "Absence reporting and dashboards",
                "Configurable sickness policies"
            }),
        new FeatureInfo(
            "recruitment",
            "user-plus",
            "Recruitment",
            "Find, track and hire the right people with a simple recruitment workflow.",
            "Give hiring activity a simple home, from open roles to candidate progress, so recruitment does not disappear into personal inboxes.",
            new[]
            {
                "Create and publish vacancies",
                "Track applicants through each stage",
                "Manage interviews and hiring decisions",
                "Convert successful candidates into employees",
                "Recruitment dashboard and pipeline",
                "Configurable recruitment workflow"
            }),
        new FeatureInfo(
            "company-documents",
            "folder-open",
            "Company Documents",
            "Share company policies, collect acknowledgements and keep important documents organised.",
            "Store company policies, templates and employee-facing documents where teams can find the right version without searching several folders.",
            new[]
            {
                "Store policies and company documents",
                "Request employee acknowledgements",
                "Track who has read each document",
                "Manage document versions",
                "Review and renewal reminders",
                "Secure document storage"
            }),
        new FeatureInfo(
            "workflows-reminders",
            "diagram-project",
            "Workflows & Reminders",
            "Automate routine HR tasks with reminders, approvals and scheduled actions.",
            "Turn repeat HR admin into more reliable prompts and follow-ups, helping managers stay on top of tasks that otherwise depend on memory.",
            new[]
            {
                "Automated HR tasks and reminders",
                "Employee onboarding checklists",
                "Offboarding workflows",
                "Probation review scheduling",
                "Approval tasks for managers",
                "Background notifications and alerts"
            }),
        new FeatureInfo(
            "reporting",
            "chart-line",
            "Reporting",
            "Turn your HR data into clear reports to support better business decisions.",
            "See useful people information more clearly, so leaders can understand workforce activity without stitching together separate spreadsheets.",
            new[]
            {
                "Headcount and employee reports",
                "Leave and absence reporting",
                "Recruitment activity reports",
                "Workload and HR action reports",
                "Export to Excel for further analysis",
                "Visual dashboards and trends"
            }),
    };

    private static readonly Dictionary<string, FeatureInfo> BySlug =
        All.ToDictionary(f => f.Slug, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string slug, out FeatureInfo? feature) =>
        BySlug.TryGetValue(slug, out feature);
}
