using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed class ListOrganisationDataExportsHandler(
    ReportingDbContext db,
    IClock clock,
    IAuthorizationService authorizationService)
{
    private const int MaxRows = 50;

    public async Task<Result<ListOrganisationDataExportsResponse>> HandleAsync(
        ListOrganisationDataExportsRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Ticket 5: Organisation data exports require BOTH Company Administrator AND HR Administrator roles.
        // Company Administrator alone (without HR Admin role) is insufficient.
        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(userId, cancellationToken);

        // SystemRoles.HrAdministrator = 00000000-0000-0000-0000-000000000004
        // SystemRoles.CompanyAdministrator = 00000000-0000-0000-0000-000000000006
        var hrAdministratorRoleId = new Guid("00000000-0000-0000-0000-000000000004");
        var companyAdministratorRoleId = new Guid("00000000-0000-0000-0000-000000000006");

        var hasHrAdminRole = effectiveRoles.Contains(hrAdministratorRoleId);
        var hasCompanyAdminRole = effectiveRoles.Contains(companyAdministratorRoleId);

        if (!hasHrAdminRole || !hasCompanyAdminRole)
        {
            return Result.Failure<ListOrganisationDataExportsResponse>(
                Error.Forbidden("This action requires both Company Administrator and HR Administrator roles."));
        }

        var now = clock.UtcNowOffset();

        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .OrderByDescending(e => e.RequestedAt)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(e => new OrganisationDataExportListItem(
                e.Id,
                e.Status,
                e.RequestedAt,
                e.CompletedAt,
                e.ExpiresAt,
                e.FileSizeBytes,
                e.DownloadCount,
                e.IsDownloadable(now)))
            .ToList();

        return Result.Success(new ListOrganisationDataExportsResponse(items));
    }
}
