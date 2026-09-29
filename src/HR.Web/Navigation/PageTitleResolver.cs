namespace HR.Web.Navigation;

public static class PageTitleResolver
{
    public const string Fallback = "One Big Team";

    private sealed record Resource(string Plural, string Singular);

    private static readonly Dictionary<string, Resource> Resources = new(StringComparer.Ordinal)
    {
        ["employees"] = new("Employees", "Employee"),
        ["departments"] = new("Departments", "Department"),
        ["locations"] = new("Locations", "Location"),
        ["location-types"] = new("Location Types", "Location Type"),
        ["position-profiles"] = new("Position Profiles", "Position Profile"),
        ["onboarding-templates"] = new("Onboarding Templates", "Onboarding Template"),
        ["employment-types"] = new("Employment Types", "Employment Type"),
        ["assets"] = new("Assets", "Asset"),
        ["asset-categories"] = new("Asset Categories", "Asset Category"),
        ["leave-types"] = new("Leave Types", "Leave Type"),
        ["leave-policies"] = new("Leave Policies", "Leave Policy"),
        ["public-holidays"] = new("Public Holidays", "Public Holiday"),
        ["document-types"] = new("Document Types", "Document Type"),
        ["sickness-categories"] = new("Sickness Categories", "Sickness Category"),
        ["vacancies"] = new("Vacancies", "Vacancy"),
        ["candidates"] = new("Candidates", "Candidate"),
        ["external-recruiters"] = new("External Recruiters", "External Recruiter"),
        ["recruitment-stages"] = new("Recruitment Stages", "Recruitment Stage"),
    };

    private static readonly Dictionary<string, string> ReportTitles = new(StringComparer.Ordinal)
    {
        ["document-acknowledgement"] = "Document Acknowledgement Report",
        ["document-compliance"] = "Document Compliance Report",
        ["asset-assignment"] = "Asset Assignment Report",
        ["employee-directory"] = "Employee Directory Report",
        ["employee-starters"] = "Employee Starters Report",
        ["employee-leavers"] = "Employee Leavers Report",
        ["hr-headcount-summary"] = "Headcount Summary Report",
        ["leave-summary"] = "Leave Summary Report",
        ["leave-calendar"] = "Leave Calendar Report",
        ["probation"] = "Probation Report",
        ["onboarding-progress"] = "Onboarding Progress Report",
        ["offboarding-progress"] = "Offboarding Progress Report",
        ["workload-actions"] = "Workload & Actions Report",
        ["recruitment-pipeline-summary"] = "Recruitment Pipeline Summary Report",
        ["recruitment-pipeline"] = "Recruitment Pipeline Report",
        ["vacancy-performance"] = "Vacancy Performance Report",
        ["sickness"] = "Sickness Report",
    };

    private static readonly Dictionary<string, string> ExactPaths = new(StringComparer.Ordinal)
    {
        ["dashboard/hr"] = "HR Dashboard",
        ["dashboard/recruitment"] = "Recruitment Dashboard",
        ["dashboard/manager"] = "Manager Dashboard",
        ["getting-started"] = "Getting Started",
        ["subscription"] = "Subscription & Billing",
        ["access-denied"] = "Access Denied",
        ["forgot-password"] = "Forgot Password",
        ["reset-password-complete"] = "Password Reset",
        ["verify-email-error"] = "Email Verification",
        ["login"] = "Sign In",
        ["not-found"] = "Page Not Found",
        ["error"] = "Error",
    };

    public static string Resolve(string? absolutePathOrUri)
    {
        if (string.IsNullOrWhiteSpace(absolutePathOrUri))
            return Fallback;

        var path = absolutePathOrUri.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs))
            path = abs.AbsolutePath;

        path = path.Trim('/');
        if (path.Length == 0)
            return Fallback;

        var lower = path.ToLowerInvariant();
        if (ExactPaths.TryGetValue(lower, out var exact))
            return exact;

        var segments = lower.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments[0] == "invite")
            return "Accept Invitation";

        if (segments.Length >= 3 && segments[0] == "companies" && Guid.TryParse(segments[1], out _))
            return ResolveCompanyScoped(segments[2..]);

        return Fallback;
    }

    private static string ResolveCompanyScoped(string[] s)
    {
        var resource = s[0];

        switch (resource)
        {
            case "edit":
            case "view":
                return "Company Profile";
            case "hr-settings":
                return "HR Settings";
            case "organisation-chart":
                return "Organisation Chart";
            case "data-import":
                return "Data Import";
            case "hr":
                return s.Length >= 2 && s[1] == "inbox" ? "HR Inbox" : Fallback;
            case "tasks":
                return "Task";
            case "reporting":
                if (s.Length == 1)
                    return "Reporting";
                return ReportTitles.TryGetValue(s[1], out var report) ? report : "Report";
            case "support":
                if (s.Length == 1)
                    return "Help & Feedback";
                return s[1] == "admin" ? "Support Requests" : "Support Request";
            case "user-administration":
                return s.Length == 1 ? "User Administration" : "User";
            case "shared-documents":
                if (s.Length == 1)
                    return "Shared Documents";
                if (s.Length >= 3 && s[2] == "acknowledgement-progress")
                    return "Acknowledgement Progress";
                if (s[1] == "published")
                    return "Document Acknowledgement";
                return "Shared Document";
            case "employees":
                if (s.Length == 1)
                    return "Employees";
                if (s[1] == "new")
                    return "New Employee";
                if (s.Length >= 3 && s[2] == "profile")
                    return "Employee Profile";
                return "Employee";
            case "vacancies":
                if (s.Length >= 3 && s[2] == "kanban")
                    return "Vacancy Pipeline";
                break;
        }

        if (Resources.TryGetValue(resource, out var res))
        {
            if (s.Length == 1)
                return res.Plural;
            if (s[1] == "new")
                return $"New {res.Singular}";
            return res.Singular;
        }

        return Fallback;
    }
}
