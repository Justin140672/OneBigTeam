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
                "Employee records and employment history — stop hunting across spreadsheets and old emails for who someone reports to or when they started",
                "Departments, locations and position profiles — see how your organisation is actually structured, not just a flat employee list",
                "Documents and document requests — know exactly which documents you're missing for each employee instead of chasing them ad hoc",
                "Compensation and employment changes — keep a reliable history of pay and role changes instead of relying on memory or old contracts",
                "Notes and audit history — have a clear record of what happened and when, useful when questions come up later",
                "Employee self-service profile — let employees update their own details, cutting down requests landing in HR's inbox"
            }),
        new FeatureInfo(
            "leave-management",
            "calendar-days",
            "Leave Management",
            "Request, approve and track annual leave without spreadsheets or email.",
            "Bring leave requests, balances and approvals into a shared process so everyone understands who is away and what needs action.",
            new[]
            {
                "Annual leave requests and approvals — replace the email chain of \"can I take this day off\" with a clear, trackable request",
                "Configurable leave types and allowances — match the policy your business actually runs, not a generic default",
                "Team calendars and leave visibility — know at a glance who's away, so you can plan cover without asking around",
                "Leave balances and adjustments — stop manually tallying days in a spreadsheet that's always slightly out of date",
                "Public holiday support — avoid disputes over bank holidays being miscounted against someone's allowance",
                "Approval workflows and reminders — make sure requests don't sit unanswered in a manager's inbox"
            }),
        new FeatureInfo(
            "sickness-absence",
            "heart-pulse",
            "Sickness & Absence",
            "Record sickness, return-to-work meetings and absence trends.",
            "Track sickness and other absence clearly, so managers can respond with better context and keep records complete without relying on inbox history.",
            new[]
            {
                "Record sickness and absence events — build a complete, consistent record instead of scattered notes across managers",
                "Return-to-work meetings and notes — make sure every absence gets a proper follow-up, not just the long ones",
                "Fit notes and supporting documents — keep the paperwork attached to the right person, not lost in an inbox",
                "Bradford Factor and absence trends — spot absence patterns before they escalate, rather than noticing only after the fact",
                "Absence reporting and dashboards — see which teams are being affected most, without building the report by hand",
                "Configurable sickness policies — apply the policy your business runs, consistently, across every manager"
            }),
        new FeatureInfo(
            "recruitment",
            "user-plus",
            "Recruitment",
            "Find, track and hire the right people with a simple recruitment workflow.",
            "Give hiring activity a simple home, from open roles to candidate progress, so recruitment does not disappear into personal inboxes.",
            new[]
            {
                "Create and publish vacancies — get a role in front of candidates without juggling a separate job board account",
                "Track applicants through each stage — always know where each candidate stands, instead of digging through email threads",
                "Manage interviews and hiring decisions — keep interview feedback in one place so decisions aren't lost between people",
                "Convert successful candidates into employees — skip re-entering the same details once someone accepts an offer",
                "Recruitment dashboard and pipeline — see how hiring is progressing across all your open roles at a glance",
                "Configurable recruitment workflow — match the stages your business actually uses to hire"
            }),
        new FeatureInfo(
            "company-documents",
            "folder-open",
            "Company Documents",
            "Share company policies, collect acknowledgements and keep important documents organised.",
            "Store company policies, templates and employee-facing documents where teams can find the right version without searching several folders.",
            new[]
            {
                "Store policies and company documents — give employees one place to find the current version, not five folders",
                "Request employee acknowledgements — get confirmation that policies have actually been read, not just circulated",
                "Track who has read each document — answer \"has everyone seen this?\" without chasing people individually",
                "Manage document versions — avoid the confusion of someone working from an outdated policy",
                "Review and renewal reminders — get prompted before a policy goes stale instead of finding out too late",
                "Secure document storage — keep sensitive company documents access-controlled rather than emailed around"
            }),
        new FeatureInfo(
            "workflows-reminders",
            "diagram-project",
            "Workflows & Reminders",
            "Automate routine HR tasks with reminders, approvals and scheduled actions.",
            "Turn repeat HR admin into more reliable prompts and follow-ups, helping managers stay on top of tasks that otherwise depend on memory.",
            new[]
            {
                "Automated HR tasks and reminders — stop routine admin depending on someone remembering to do it",
                "Employee onboarding checklists — make sure every new starter gets the same complete setup, every time",
                "Offboarding workflows — avoid loose ends like access or kit not being handled when someone leaves",
                "Probation review scheduling — never let a probation date quietly pass unreviewed",
                "Approval tasks for managers — give managers a clear queue of what needs their attention, not a buried email",
                "Background notifications and alerts — get flagged to things that need action before they become a problem"
            }),
        new FeatureInfo(
            "reporting",
            "chart-line",
            "Reporting",
            "Turn your HR data into clear reports to support better business decisions.",
            "See useful people information more clearly, so leaders can understand workforce activity without stitching together separate spreadsheets.",
            new[]
            {
                "Headcount and employee reports — answer basic \"how many people do we have, and where\" questions without a manual count",
                "Leave and absence reporting — understand absence patterns across the business, not just one team at a time",
                "Recruitment activity reports — see how hiring is actually going without asking each hiring manager individually",
                "Workload and HR action reports — spot where HR admin is piling up before it becomes a backlog",
                "Export to Excel for further analysis — take the data further when you need a one-off analysis or board report",
                "Visual dashboards and trends — see the story in the data at a glance, not just a table of numbers"
            }),
    };

    private static readonly Dictionary<string, FeatureInfo> BySlug =
        All.ToDictionary(f => f.Slug, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string slug, out FeatureInfo? feature) =>
        BySlug.TryGetValue(slug, out feature);
}
