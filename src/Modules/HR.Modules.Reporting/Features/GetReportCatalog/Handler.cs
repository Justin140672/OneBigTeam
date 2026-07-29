using HR.Modules.Reporting.Domain;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed class GetReportCatalogHandler
{
    // Static catalog — phase 1 proved the permission-filtered pattern; phase 2 (Reporting
    // Dashboard epic, OBT-704..707) adds the four new reports below. RequiresEmployeeStarterAccess
    // and RequiresLeaveSummaryAccess are additional visibility gates layered on top of the
    // existing Category-based canViewRecruitment/canViewHr split, since those two reports use
    // dedicated combined-role policies (reporting:view-employee-starter,
    // reporting:view-leave-summary) rather than a plain Recruitment/Hr category split.
    private static readonly IReadOnlyList<(string Id, string DisplayName, ReportCategory Category, string Description, bool RequiresEmployeeStarterAccess, bool RequiresLeaveSummaryAccess, bool RequiresProbationAccess, bool RequiresOnboardingAccess, bool RequiresWorkloadActionsAccess)> Catalog =
    [
        ("recruitment-pipeline-summary", "Recruitment Pipeline Summary", ReportCategory.Recruitment,
            "Overview of open vacancies and candidates by pipeline stage.", false, false, false, false, false),
        ("hr-headcount-summary", "HR Headcount Summary", ReportCategory.Hr,
            "Company-wide headcount broken down by department and status.", false, false, false, false, false),
        ("employee-directory", "Employee Directory", ReportCategory.Hr,
            "Full employee directory including department, position, manager, employment type, start date, status, work location and email.", false, false, false, false, false),
        ("employee-starters", "Employee Starter Report", ReportCategory.Hr,
            "New starters including start date, recruiter, department, position, onboarding status and probation status.", true, false, false, false, false),
        ("employee-leavers", "Employee Leaver Report", ReportCategory.Hr,
            "Leavers including leaving date, last working day, department, position, offboarding completion and account status.", false, false, false, false, false),
        ("leave-summary", "Leave Summary Report", ReportCategory.Hr,
            "Entitlement, booked, approved, remaining balance and pending requests, grouped by employee, department or leave type.", false, true, false, false, false),
        ("leave-calendar", "Leave Calendar Export", ReportCategory.Hr,
            "Employee leave calendar for a given month, filterable by department — export-oriented.", false, false, false, false, false),

        // Reporting Dashboard epic, phase 3 (OBT-708..711).
        ("sickness-report", "Sickness Report", ReportCategory.Hr,
            "Absence count, days absent and Bradford score, grouped by department or employee, with date range filtering.", false, false, false, false, false),
        ("recruitment-pipeline-report", "Recruitment Pipeline Report", ReportCategory.Recruitment,
            "Vacancies, applicants, interviews, offers and hires grouped by recruiter or vacancy.", false, false, false, false, false),
        ("vacancy-performance-report", "Vacancy Performance Report", ReportCategory.Recruitment,
            "Per-vacancy days open, applicant count, interview count, offer count and hire date.", false, false, false, false, false),
        ("probation-report", "Probation Report", ReportCategory.Hr,
            "Current probation, due and overdue reviews, passed and extended, visible to HR company-wide and to Managers for their own direct reports.", false, false, true, false, false),

        // Reporting Dashboard epic, phase 4 (OBT-712..715).
        ("onboarding-progress", "Onboarding Progress Report", ReportCategory.Hr,
            "Onboarding plan status and outstanding tasks per employee, visible to HR company-wide and to Managers for their own direct reports.", false, false, false, true, false),
        ("offboarding-progress", "Offboarding Progress Report", ReportCategory.Hr,
            "Offboarding plan status, outstanding tasks, access and asset return status per employee.", false, false, false, false, false),
        ("document-compliance", "Document Compliance Report", ReportCategory.Hr,
            "Required document coverage per employee, filterable by position profile, including missing and expiring documents.", false, false, false, false, false),
        ("document-acknowledgement", "Company Document Acknowledgement Report", ReportCategory.Hr,
            "Acknowledgement status per employee for every published company document that requires acknowledgement.", false, false, false, false, false),
        ("asset-assignment", "Asset Assignment Report", ReportCategory.Hr,
            "Assets assigned to employees including serial number, assigned date and return status.", false, false, false, false, false),

        // Reporting Dashboard epic, OBT-721. Unlike every other entry above, this report is
        // relevant to all three baseline reporting:view roles (Manager, Recruiter, HrAdministrator)
        // at once — a plain Category-based Hr/Recruitment split would wrongly hide it from a
        // Manager who has neither role. RequiresWorkloadActionsAccess is gated on the same
        // reporting:view policy that already gates the whole catalog endpoint, so effectively every
        // caller who can open the catalog at all sees this entry; the real per-category filtering
        // happens inside GetWorkloadActions/Handler.cs via each IWorkloadActionProvider.
        ("workload-actions", "Workload & HR Actions Report", ReportCategory.Hr,
            "Consolidated outstanding people-related actions across leave, sickness, probation, onboarding, offboarding, documents, assets, identity, recruitment and tasks, scoped to what the caller is permitted to see.", false, false, false, false, true),
    ];

    public Task<Result<GetReportCatalogResponse>> HandleAsync(
        GetReportCatalogRequest request,
        bool canViewRecruitment,
        bool canViewHr,
        bool canViewEmployeeStarter,
        bool canViewLeaveSummary,
        bool canViewProbation,
        bool canViewOnboarding,
        bool canViewWorkloadActions,
        CancellationToken cancellationToken)
    {
        var items = Catalog
            .Where(entry =>
            {
                if (entry.RequiresWorkloadActionsAccess) return canViewWorkloadActions;
                if (entry.RequiresEmployeeStarterAccess) return canViewEmployeeStarter;
                if (entry.RequiresLeaveSummaryAccess) return canViewLeaveSummary;
                if (entry.RequiresProbationAccess) return canViewProbation;
                if (entry.RequiresOnboardingAccess) return canViewOnboarding;

                return entry.Category switch
                {
                    ReportCategory.Recruitment => canViewRecruitment,
                    ReportCategory.Hr => canViewHr,
                    _ => false,
                };
            })
            .Select(entry => new ReportCatalogItem(entry.Id, entry.DisplayName, entry.Category.ToString(), entry.Description))
            .ToList();

        return Task.FromResult(Result.Success(new GetReportCatalogResponse(items)));
    }
}
