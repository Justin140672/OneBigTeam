using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;
using HR.Modules.Reporting.Features.GetEmployeeLeaverReport;
using HR.Modules.Reporting.Features.GetEmployeeStarterReport;
using HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;
using HR.Modules.Reporting.Features.GetLeaveCalendarReport;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.Modules.Reporting.Features.GetWorkloadActions;

namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Single source of truth for the report catalogue. This is the registry required by REP-03:
/// GetReportCatalog, SaveReportView, AddReportFavourite, GetReportViews and GetReportFavourites
/// all consult this instead of maintaining their own copies of report ids/categories/access gates.
///
/// Supported filter/grouping/sorting field names are derived by reflecting over each report's own
/// Request record (excluding CompanyId), so this stays in sync with each report's Validator without
/// duplicating field lists by hand. Enum-typed fields automatically restrict allowed values to the
/// enum's member names; a small number of string-typed fields with a bespoke allowed-value list
/// (e.g. GetWorkloadActions' GroupBy) are given explicit overrides below.
/// </summary>
internal static class ReportCatalog
{
    private static readonly string[] WorkloadActionsGroupByValues =
        ["ActionType", "AssignedUser", "Department", "DueDate"];

    private sealed record Entry(
        string Id,
        string DisplayName,
        ReportCategory Category,
        string Description,
        ReportAccessGate AccessGate,
        Type RequestType,
        ReportSensitivity Sensitivity,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>?>? FieldOverrides = null);

    private static readonly IReadOnlyList<Entry> Entries =
    [
        // Standard: company-wide aggregates only, no named individuals in the exported rows.
        new("recruitment-pipeline-summary", "Recruitment Pipeline Summary", ReportCategory.Recruitment,
            "Overview of open vacancies and candidates by pipeline stage.", ReportAccessGate.Recruitment,
            typeof(GetRecruitmentPipelineSummaryReportRequest), ReportSensitivity.Standard),

        // Sensitive: exported rows include employee names (see ExportHrHeadcountSummaryReport/Handler.cs),
        // despite the aggregate-sounding name — an explicit exception to the "aggregate = Standard" default.
        new("hr-headcount-summary", "HR Headcount Summary", ReportCategory.Hr,
            "Company-wide headcount broken down by department and status.", ReportAccessGate.Hr,
            typeof(GetHrHeadcountSummaryReportRequest), ReportSensitivity.Sensitive),

        new("employee-directory", "Employee Directory", ReportCategory.Hr,
            "Full employee directory including department, position, manager, employment type, start date, status, work location and email.",
            ReportAccessGate.Hr, typeof(GetEmployeeDirectoryReportRequest), ReportSensitivity.Sensitive),

        new("employee-starters", "Employee Starter Report", ReportCategory.Hr,
            "New starters including start date, recruiter, department, position, onboarding status and probation status.",
            ReportAccessGate.EmployeeStarter, typeof(GetEmployeeStarterReportRequest), ReportSensitivity.Sensitive),

        new("employee-leavers", "Employee Leaver Report", ReportCategory.Hr,
            "Leavers including leaving date, last working day, department, position, offboarding completion and account status.",
            ReportAccessGate.Hr, typeof(GetEmployeeLeaverReportRequest), ReportSensitivity.Sensitive),

        new("leave-summary", "Leave Summary Report", ReportCategory.Hr,
            "Entitlement, booked, approved, remaining balance and pending requests, grouped by employee, department or leave type.",
            ReportAccessGate.LeaveSummary, typeof(GetLeaveSummaryReportRequest), ReportSensitivity.Sensitive),

        new("leave-calendar", "Leave Calendar Export", ReportCategory.Hr,
            "Employee leave calendar for a given month, filterable by department — export-oriented.",
            ReportAccessGate.Hr, typeof(GetLeaveCalendarReportRequest), ReportSensitivity.Sensitive),

        new("sickness-report", "Sickness Report", ReportCategory.Hr,
            "Absence count, days absent and Bradford score, grouped by department or employee, with date range filtering.",
            ReportAccessGate.Hr, typeof(GetSicknessReportRequest), ReportSensitivity.Sensitive),

        // Standard: recruiter/vacancy-grouped counts only, no named candidates in the exported rows.
        new("recruitment-pipeline-report", "Recruitment Pipeline Report", ReportCategory.Recruitment,
            "Vacancies, applicants, interviews, offers and hires grouped by recruiter or vacancy.",
            ReportAccessGate.Recruitment, typeof(GetRecruitmentPipelineReportRequest), ReportSensitivity.Standard),

        // Standard: per-vacancy aggregate counts only, no named candidates in the exported rows.
        new("vacancy-performance-report", "Vacancy Performance Report", ReportCategory.Recruitment,
            "Per-vacancy days open, applicant count, interview count, offer count and hire date.",
            ReportAccessGate.Recruitment, typeof(GetVacancyPerformanceReportRequest), ReportSensitivity.Standard),

        new("probation-report", "Probation Report", ReportCategory.Hr,
            "Current probation, due and overdue reviews, passed and extended, visible to HR company-wide and to Managers for their complete reporting hierarchy.",
            ReportAccessGate.Probation, typeof(GetProbationReportRequest), ReportSensitivity.Sensitive),

        new("onboarding-progress", "Onboarding Progress Report", ReportCategory.Hr,
            "Onboarding plan status and outstanding tasks per employee, visible to HR company-wide and to Managers for their complete reporting hierarchy.",
            ReportAccessGate.Onboarding, typeof(GetOnboardingProgressReportRequest), ReportSensitivity.Sensitive),

        new("offboarding-progress", "Offboarding Progress Report", ReportCategory.Hr,
            "Offboarding plan status, outstanding tasks, access and asset return status per employee.",
            ReportAccessGate.Hr, typeof(GetOffboardingProgressReportRequest), ReportSensitivity.Sensitive),

        new("document-compliance", "Document Compliance Report", ReportCategory.Hr,
            "Required document coverage per employee, filterable by position profile, including missing and expiring documents.",
            ReportAccessGate.Hr, typeof(GetDocumentComplianceReportRequest), ReportSensitivity.Sensitive),

        new("document-acknowledgement", "Company Document Acknowledgement Report", ReportCategory.Hr,
            "Acknowledgement status per employee for every published company document that requires acknowledgement.",
            ReportAccessGate.Hr, typeof(GetCompanyDocumentAcknowledgementReportRequest), ReportSensitivity.Sensitive),

        new("asset-assignment", "Asset Assignment Report", ReportCategory.Hr,
            "Assets assigned to employees including serial number, assigned date and return status.",
            ReportAccessGate.Hr, typeof(GetAssetAssignmentReportRequest), ReportSensitivity.Sensitive),

        // Standard: anonymous workforce aggregates only — counts and percentages, no named
        // individuals, no drill-through. Gated on the dedicated reporting:view-equality permission
        // rather than ReportAccessGate.Hr so general HR reporting access never exposes it.
        new("equality-diversity", "Equality & Diversity Report", ReportCategory.Hr,
            "Anonymous workforce equality statistics by gender, age band, ethnicity, disability, sexual orientation, religion or belief and caring responsibilities, with small groups suppressed.",
            ReportAccessGate.EqualityDiversity, typeof(EqualityDiversityReportCatalogRequest), ReportSensitivity.Standard),

        new("workload-actions", "Workload & HR Actions Report", ReportCategory.Hr,
            "Consolidated outstanding people-related actions across leave, sickness, probation, onboarding, offboarding, documents, assets, identity, recruitment and tasks, scoped to what the caller is permitted to see.",
            ReportAccessGate.WorkloadActions, typeof(GetWorkloadActionsRequest), ReportSensitivity.Sensitive,
            new Dictionary<string, IReadOnlyCollection<string>?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GroupBy"] = WorkloadActionsGroupByValues,
            }),
    ];

    public static readonly IReadOnlyDictionary<string, ReportDefinition> Definitions =
        Entries.ToDictionary(e => e.Id, BuildDefinition, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<ReportDefinition> All => Definitions.Values;

    public static bool TryGet(string reportId, out ReportDefinition definition)
    {
        if (string.IsNullOrEmpty(reportId))
        {
            definition = null!;
            return false;
        }

        return Definitions.TryGetValue(reportId, out definition!);
    }

    private static ReportDefinition BuildDefinition(Entry entry)
    {
        var fields = new Dictionary<string, IReadOnlyCollection<string>?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in entry.RequestType.GetProperties())
        {
            if (string.Equals(property.Name, "CompanyId", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.FieldOverrides is not null && entry.FieldOverrides.TryGetValue(property.Name, out var overrideValues))
            {
                fields[property.Name] = overrideValues;
                continue;
            }

            var propertyType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            fields[property.Name] = underlyingType.IsEnum
                ? Enum.GetNames(underlyingType)
                : null;
        }

        return new ReportDefinition(entry.Id, entry.DisplayName, entry.Category, entry.Description, entry.AccessGate, fields, entry.Sensitivity);
    }
}
