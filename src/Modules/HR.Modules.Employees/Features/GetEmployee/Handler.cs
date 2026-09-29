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

    public async Task<Result<GetEmployeeResponse>> HandleAsync(
        GetEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        // Only the employee themself or an HR Administrator may view this full HR record — a
        // manager in the target's reporting hierarchy is deliberately excluded here and served
        // instead by the separate, field-restricted GetEmployeeTeamView endpoint (its own
        // operational-only database projection, no personal/HR-only columns fetched at all). This
        // must be checked before any of the record's fields are read, not left to the UI to hide.
        var isAuthorized = await _resourceAuthorizer.CanViewFullRecordAsync(
            request.CompanyId, request.CallerEmployeeId, request.Id, cancellationToken);
        if (!isAuthorized)
            return Result.Failure<GetEmployeeResponse>(
                Error.Forbidden("You are not authorized to view this employee's record."));

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
            return Result.Failure<GetEmployeeResponse>(
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

        await Task.WhenAll(onboardingStatusTask, probationStatusTask, offboardingStatusTask, effectiveNoticePeriodTask);

        var onboardingStatus = onboardingStatusTask.Result;
        var probationStatus = probationStatusTask.Result;
        var offboardingStatus = offboardingStatusTask.Result;
        var effectiveNoticePeriod = effectiveNoticePeriodTask.Result;

        var reportingChain = await BuildReportingChainAsync(
            request.CompanyId, result.Id, result.ManagerId, cancellationToken);
        var hasAnyLeavingProcess = await _dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .AnyAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == result.Id,
                cancellationToken);
        var hasInProgressLeavingProcess = await _dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .AnyAsync(
                p => p.CompanyId == request.CompanyId
                    && p.EmployeeId == result.Id
                    && p.Status == LeavingProcessStatus.InProgress,
                cancellationToken);

        var showOnboardingTab = onboardingStatus is not null && onboardingStatus.Status != "Completed";
        var showProbationTab = probationStatus is not null
            && probationStatus.Status is "Active" or "ReviewDue" or "Extended";
        var showOffboardingTab = offboardingStatus is not null
            && offboardingStatus.Status is not ("Completed" or "Cancelled");
        var showLeavingTab = hasAnyLeavingProcess;
        var canStartLeavingProcess = !hasInProgressLeavingProcess;

        return Result.Success(new GetEmployeeResponse(
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
