using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeTeamView;

/// <summary>
/// Manager-facing counterpart to GetEmployeeHandler. Deliberately has no dependency on
/// IEffectiveNoticePeriodResolver — notice period is not in the approved manager field set, so
/// this handler never resolves it, and its database projection below never selects a personal-
/// contact, demographic, address, HR-notes, leaving-process, notice-period or system-access
/// column. This is a genuinely leaner query, not the full GetEmployee query with fields discarded
/// afterwards — see 26-permissions-access-ux.md's field-level access matrix.
/// </summary>
internal sealed class GetEmployeeTeamViewHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IOnboardingStatusReader _onboardingStatusReader;
    private readonly IProbationStatusReader _probationStatusReader;
    private readonly IOffboardingStatusReader _offboardingStatusReader;
    private readonly EmployeesResourceAuthorizer _resourceAuthorizer;
    private readonly IProfilePhotoReader _profilePhotoReader;

    public GetEmployeeTeamViewHandler(
        EmployeesDbContext dbContext,
        IOnboardingStatusReader onboardingStatusReader,
        IProbationStatusReader probationStatusReader,
        IOffboardingStatusReader offboardingStatusReader,
        EmployeesResourceAuthorizer resourceAuthorizer,
        IProfilePhotoReader profilePhotoReader)
    {
        _dbContext = dbContext;
        _onboardingStatusReader = onboardingStatusReader;
        _probationStatusReader = probationStatusReader;
        _offboardingStatusReader = offboardingStatusReader;
        _resourceAuthorizer = resourceAuthorizer;
        _profilePhotoReader = profilePhotoReader;
    }

    public async Task<Result<GetEmployeeTeamViewResponse>> HandleAsync(
        GetEmployeeTeamViewRequest request,
        CancellationToken cancellationToken)
    {
        var isAuthorized = await _resourceAuthorizer.CanViewAsManagerAsync(
            request.CompanyId, request.CallerEmployeeId, request.Id, cancellationToken);
        if (!isAuthorized)
            return Result.Failure<GetEmployeeTeamViewResponse>(
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
                e.StartDate,
                e.Status,
                e.EmployeeNumber,
                e.EmploymentTypeId,
                EmploymentTypeName = _dbContext.EmploymentTypes
                    .Where(t => t.Id == e.EmploymentTypeId)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
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
                ManagerFullName = _dbContext.Employees
                    .Where(m => m.Id == e.ManagerId)
                    .Select(m => (m.PreferredName ?? m.FirstName) + " " + m.LastName)
                    .FirstOrDefault(),
                DirectReportsCount = _dbContext.Employees
                    .Count(r => r.ManagerId == e.Id && r.Status != EmploymentStatus.FormerEmployee),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return Result.Failure<GetEmployeeTeamViewResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        if (result.Status == EmploymentStatus.FormerEmployee)
        {
            return Result.Failure<GetEmployeeTeamViewResponse>(
                Error.Forbidden("You are not authorized to view this employee's record."));
        }

        var onboardingStatusTask = _onboardingStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);
        var probationStatusTask = _probationStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);
        var offboardingStatusTask = _offboardingStatusReader.GetStatusAsync(
            request.CompanyId, result.Id, cancellationToken);

        await Task.WhenAll(onboardingStatusTask, probationStatusTask, offboardingStatusTask);

        var onboardingStatus = onboardingStatusTask.Result;
        var probationStatus = probationStatusTask.Result;
        var offboardingStatus = offboardingStatusTask.Result;

        var reportingChain = await BuildReportingChainAsync(
            request.CompanyId, result.Id, result.ManagerId, cancellationToken);
        var hasAnyLeavingProcess = await _dbContext.EmployeeLeavingProcesses
            .AsNoTracking()
            .AnyAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == result.Id,
                cancellationToken);

        var photoUrls = await _profilePhotoReader.GetCurrentPhotoUrlsAsync(
            request.CompanyId, [result.Id], cancellationToken);

        var showOnboardingTab = onboardingStatus is not null && onboardingStatus.Status != "Completed";
        var showProbationTab = probationStatus is not null
            && probationStatus.Status is "Active" or "ReviewDue" or "Extended";
        var showOffboardingTab = offboardingStatus is not null
            && offboardingStatus.Status is not ("Completed" or "Cancelled");
        var showLeavingTab = hasAnyLeavingProcess;

        return Result.Success(new GetEmployeeTeamViewResponse(
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
            photoUrls.TryGetValue(result.Id, out var photoUrl) ? photoUrl : null));
    }

    private async Task<IReadOnlyList<ReportingChainItem>> BuildReportingChainAsync(
        Guid companyId, Guid employeeId, Guid? managerId, CancellationToken cancellationToken)
    {
        if (managerId is null)
            return [];

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.PreferredName, e.ManagerId, e.PositionProfileId })
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

            chain.Add(new ReportingChainItem(manager.Id, PersonName.Display(manager.FirstName, manager.LastName, manager.PreferredName), jobTitle));
            currentManagerId = manager.ManagerId;
        }

        chain.Reverse();
        return chain;
    }
}
