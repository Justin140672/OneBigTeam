using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed class GetEmployeeHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IOnboardingStatusReader _onboardingStatusReader;
    private readonly IProbationStatusReader _probationStatusReader;
    private readonly IOffboardingStatusReader _offboardingStatusReader;
    private readonly IEffectiveNoticePeriodResolver _effectiveNoticePeriodResolver;
    private readonly EmployeesResourceAuthorizer _resourceAuthorizer;

    public GetEmployeeHandler(
        EmployeesDbContext dbContext,
        IOnboardingStatusReader onboardingStatusReader,
        IProbationStatusReader probationStatusReader,
        IOffboardingStatusReader offboardingStatusReader,
        IEffectiveNoticePeriodResolver effectiveNoticePeriodResolver,
        EmployeesResourceAuthorizer resourceAuthorizer)
    {
        _dbContext = dbContext;
        _onboardingStatusReader = onboardingStatusReader;
        _probationStatusReader = probationStatusReader;
        _offboardingStatusReader = offboardingStatusReader;
        _effectiveNoticePeriodResolver = effectiveNoticePeriodResolver;
        _resourceAuthorizer = resourceAuthorizer;
    }

    public async Task<Result<object>> HandleAsync(
        GetEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        // IAM-07: only the employee themself, a manager anywhere in their reporting hierarchy, or
        // an HR Administrator may view this employee's record. This must be checked before any
        // of that record's fields are read, not left to the UI to hide — see the GetEmployee
        // security ticket this closes.
        var isAuthorized = await _resourceAuthorizer.CanViewAsync(
            request.CompanyId, request.CallerEmployeeId, request.Id, cancellationToken);
        if (!isAuthorized)
            return Result.Failure<object>(
                Error.Forbidden("You are not authorized to view this employee's record."));

        // Field-level access matrix: a manager (the only remaining authorized path besides self/HR-admin) is
        // restricted to the operational-only field subset — GetEmployeeManagerResponse — rather
        // than the full HR record. See EmployeeViewScope/ResolveViewScopeAsync's remarks and
        // 30-administrative-role-separation-matrix.md (reconciled with 26-permissions-access-ux.md
        // to this one field list). The projection happens here, before the response leaves the
        // handler — never fetch the full record and hide fields client-side.
        var viewScope = await _resourceAuthorizer.ResolveViewScopeAsync(
            request.CallerEmployeeId, request.Id, cancellationToken);

        var result = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == request.Id && e.CompanyId == request.CompanyId)
            .Select(e => new
            {
                e.Id,
                e.CompanyId,
                e.DepartmentId,
                e.LocationId,
                e.PositionProfileId,
                e.ManagerId,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.WorkEmail,
                e.PersonalEmail,
                e.StartDate,
                e.DateOfBirth,
                e.Nationality,
                e.Gender,
                e.GenderOther,
                e.PhoneNumber,
                e.HomePhone,
                e.AddressLine1,
                e.AddressLine2,
                e.City,
                e.County,
                e.PostCode,
                e.Country,
                e.Status,
                e.HasSystemAccess,
                e.WorkingDaysOverride,
                e.HoursPerDayOverride,
                e.EmployeeNumber,
                e.EmploymentTypeId,
                EmploymentTypeName = _dbContext.EmploymentTypes
                    .Where(t => t.Id == e.EmploymentTypeId)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                e.ContinuousServiceDate,
                e.ProbationEndDate,
                e.LeavingDate,
                e.NoticePeriodUnitOverride,
                e.NoticePeriodLengthOverride,
                e.Notes,
                e.CreatedAt,
                e.UpdatedAt,
                e.Version,
                DepartmentName = _dbContext.Departments
                    .Where(d => d.Id == e.DepartmentId)
                    .Select(d => d.Name)
                    .FirstOrDefault(),
                LocationName = _dbContext.Locations
                    .Where(l => l.Id == e.LocationId)
                    .Select(l => l.Name)
                    .FirstOrDefault(),
                PositionTitle = _dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.Title)
                    .FirstOrDefault(),
                PositionNoticePeriodUnitOverride = _dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.NoticePeriodUnitOverride)
                    .FirstOrDefault(),
                PositionNoticePeriodLengthOverride = _dbContext.PositionProfiles
                    .Where(p => p.Id == e.PositionProfileId)
                    .Select(p => p.NoticePeriodLengthOverride)
                    .FirstOrDefault(),
                ManagerFullName = _dbContext.Employees
                    .Where(m => m.Id == e.ManagerId)
                    .Select(m => m.FirstName + " " + m.LastName)
                    .FirstOrDefault(),
                DirectReportsCount = _dbContext.Employees
                    .Count(r => r.ManagerId == e.Id && r.Status != EmploymentStatus.FormerEmployee)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return Result.Failure<object>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        var onboardingStatusTask = _onboardingStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);
        var probationStatusTask = _probationStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);
        var offboardingStatusTask = _offboardingStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);
        var effectiveNoticePeriodTask = _effectiveNoticePeriodResolver.ResolveAsync(
            request.CompanyId,
            result.NoticePeriodUnitOverride,
            result.NoticePeriodLengthOverride,
            result.PositionNoticePeriodUnitOverride,
            result.PositionNoticePeriodLengthOverride,
            cancellationToken);

        // These four all go through other modules' own DbContexts (or no DbContext at all), so
        // they're safe to run concurrently.
        await Task.WhenAll(onboardingStatusTask, probationStatusTask, offboardingStatusTask, effectiveNoticePeriodTask);

        var onboardingStatus = onboardingStatusTask.Result;
        var probationStatus = probationStatusTask.Result;
        var offboardingStatus = offboardingStatusTask.Result;
        var effectiveNoticePeriod = effectiveNoticePeriodTask.Result;

        // Sequential — both hit this same EmployeesDbContext instance, which EF Core does not
        // allow to be used by more than one in-flight operation at a time.
        var reportingChain = await BuildReportingChainAsync(
            request.CompanyId, result.Id, result.ManagerId, cancellationToken);
        // EmployeeLeavingProcess is owned by this same module/DbContext (unlike onboarding/
        // probation/offboarding status, which live in other modules and are read through
        // Infrastructure.Abstractions reader ports), so this is a direct query rather than a
        // cross-module reader.
        // Any attempt at all — not just InProgress — keeps the unified workspace reachable so a
        // completed or cancelled departure remains visible as history (SPEC-OFF-01: "Historical
        // attempts must remain accessible and clearly distinguished from the current attempt").
        var hasAnyLeavingProcess = await _dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .AnyAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == result.Id,
                cancellationToken);
        // Separate from tab visibility: gates the "Start leaving process" action, which must stay
        // available after a cancelled/completed attempt so a later departure can create a new one.
        var hasInProgressLeavingProcess = await _dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .AnyAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == result.Id
                    && p.Status == LeavingProcessStatus.InProgress,
                cancellationToken);

        // Single source of truth for "should the employee profile show this lifecycle tab" — the
        // frontend reads these fields rather than re-deriving them from separately-fetched status
        // calls, so these predicates must stay the only place this logic is expressed.
        var showOnboardingTab = onboardingStatus is not null && onboardingStatus.Status != "Completed";
        var showProbationTab = probationStatus is not null
            && probationStatus.Status is "Active" or "ReviewDue" or "Extended";
        var showOffboardingTab = offboardingStatus is not null
            && offboardingStatus.Status is not ("Completed" or "Cancelled");
        var showLeavingTab = hasAnyLeavingProcess;
        var canStartLeavingProcess = !hasInProgressLeavingProcess;

        if (viewScope == EmployeeViewScope.ManagerRestricted)
        {
            return Result.Success<object>(new GetEmployeeManagerResponse(
                result.Id,
                result.CompanyId,
                result.DepartmentId,
                result.DepartmentName,
                result.LocationId,
                result.LocationName,
                result.PositionProfileId,
                result.PositionTitle,
                result.ManagerId,
                result.ManagerFullName,
                result.DirectReportsCount,
                reportingChain,
                result.FirstName,
                result.LastName,
                result.PreferredName,
                result.WorkEmail,
                result.StartDate,
                result.Status,
                result.EmployeeNumber,
                result.EmploymentTypeId,
                result.EmploymentTypeName,
                showOnboardingTab,
                showProbationTab,
                showOffboardingTab,
                showLeavingTab,
                canStartLeavingProcess));
        }

        return Result.Success<object>(new GetEmployeeResponse(
            result.Id,
            result.CompanyId,
            result.DepartmentId,
            result.DepartmentName,
            result.LocationId,
            result.LocationName,
            result.PositionProfileId,
            result.PositionTitle,
            result.ManagerId,
            result.ManagerFullName,
            result.DirectReportsCount,
            reportingChain,
            result.FirstName,
            result.LastName,
            result.PreferredName,
            result.WorkEmail,
            result.PersonalEmail,
            result.StartDate,
            result.DateOfBirth,
            result.Nationality,
            result.Gender,
            result.GenderOther,
            result.PhoneNumber,
            result.HomePhone,
            result.AddressLine1,
            result.AddressLine2,
            result.City,
            result.County,
            result.PostCode,
            result.Country,
            result.Status,
            result.HasSystemAccess,
            result.WorkingDaysOverride,
            result.HoursPerDayOverride,
            result.EmployeeNumber,
            result.EmploymentTypeId,
            result.EmploymentTypeName,
            result.ContinuousServiceDate,
            result.ProbationEndDate,
            result.LeavingDate,
            result.NoticePeriodUnitOverride,
            result.NoticePeriodLengthOverride,
            result.Notes,
            result.CreatedAt,
            result.UpdatedAt,
            showOnboardingTab,
            showProbationTab,
            showOffboardingTab,
            showLeavingTab,
            canStartLeavingProcess,
            effectiveNoticePeriod.Unit,
            effectiveNoticePeriod.Length,
            effectiveNoticePeriod.Source,
            result.Version));
    }

    // Walks the ManagerId chain from the employee's own manager up to the root, using an
    // in-memory visited set so a corrupt/circular manager reference can't cause an infinite loop.
    private async Task<IReadOnlyList<ReportingChainItem>> BuildReportingChainAsync(
        Guid companyId, Guid employeeId, Guid? managerId, CancellationToken cancellationToken)
    {
        if (managerId is null)
            return [];

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.ManagerId, e.PositionProfileId })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var positionTitles = await _dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var chain = new List<ReportingChainItem>();
        var visited = new HashSet<Guid> { employeeId };
        var currentManagerId = managerId;

        while (currentManagerId is Guid id && visited.Add(id) && employees.TryGetValue(id, out var manager))
        {
            var jobTitle = manager.PositionProfileId is Guid profileId && positionTitles.TryGetValue(profileId, out var title)
                ? title
                : null;

            chain.Add(new ReportingChainItem(manager.Id, $"{manager.FirstName} {manager.LastName}", jobTitle));
            currentManagerId = manager.ManagerId;
        }

        chain.Reverse();
        return chain;
    }
}
