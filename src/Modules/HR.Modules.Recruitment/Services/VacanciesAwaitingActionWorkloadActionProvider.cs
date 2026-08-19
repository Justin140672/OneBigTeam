using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for vacancies awaiting recruiter action.
/// Recruitment-scoped: only callers holding the Recruiter role (reporting:view-recruitment) get
/// results — matches GetRecruitmentPipelineReport/GetVacancyPerformanceReport, which are also
/// Recruiter-only rather than shared with HR/Manager.
///
/// Interpretation note (per OBT-721 ticket guidance to document rather than block when there's no
/// clean 1:1 domain concept): Vacancy has no dedicated "assignee" employee — the closest available
/// concept is HiringManagerId, so that is used as the WorkloadAction.EmployeeId/subject here. Two
/// distinct outstanding-action shapes are surfaced: Open vacancies with no ExternalRecruiter agency
/// assigned yet ("Assign Recruiter"), and Open vacancies that do have one but have been open for
/// 30+ days with no closure ("Progress Vacancy") — there is no explicit due-date/SLA field on
/// Vacancy, so DueDate is left null (Upcoming urgency) for both.
/// </summary>
internal sealed class VacanciesAwaitingActionWorkloadActionProvider(
    RecruitmentDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Vacancies Awaiting Action";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsRecruiter = (await authorizationService.AuthorizeAsync(caller, "reporting:view-recruitment")).Succeeded;
        if (!callerIsRecruiter)
            return [];

        var openVacancies = await dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Status == VacancyStatus.Open)
            .Select(v => new { v.Id, v.HiringManagerId, v.AssignedRecruiterId, v.AdvertTitle, v.OpenedAt })
            .ToListAsync(cancellationToken);

        if (openVacancies.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, openVacancies.Select(v => v.HiringManagerId).Distinct(), cancellationToken);

        var staleCutoff = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-30);

        var actions = new List<WorkloadAction>();

        foreach (var v in openVacancies)
        {
            departments.TryGetValue(v.HiringManagerId, out var dept);
            var title = string.IsNullOrWhiteSpace(v.AdvertTitle) ? v.Id.ToString() : v.AdvertTitle;

            if (v.AssignedRecruiterId is null)
            {
                actions.Add(new WorkloadAction(
                    EmployeeId: v.HiringManagerId,
                    EmployeeName: dept?.EmployeeName ?? v.HiringManagerId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: $"Assign Recruiter — {title}",
                    ActionCategory: ActionCategory,
                    DueDate: null,
                    AssignedTo: null,
                    Status: "Awaiting Assignment",
                    DeepLinkUrl: $"/companies/{companyId}/vacancies/{v.Id}/view"));
            }
            else if (v.OpenedAt is not null && v.OpenedAt.Value < staleCutoff)
            {
                actions.Add(new WorkloadAction(
                    EmployeeId: v.HiringManagerId,
                    EmployeeName: dept?.EmployeeName ?? v.HiringManagerId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: $"Progress Stale Vacancy — {title}",
                    ActionCategory: ActionCategory,
                    DueDate: null,
                    AssignedTo: null,
                    Status: "Open 30+ Days",
                    DeepLinkUrl: $"/companies/{companyId}/vacancies/{v.Id}/view"));
            }
        }

        return actions;
    }
}
