using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed class GetLatestOrganisationDataExportHandler(
    ReportingDbContext db,
    IClock clock,
    IAuthorizationService authorizationService)
{
    public async Task<Result<GetLatestOrganisationDataExportResponse>> HandleAsync(
        GetLatestOrganisationDataExportRequest request,
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
            return Result.Failure<GetLatestOrganisationDataExportResponse>(
                Error.Forbidden("This action requires both Company Administrator and HR Administrator roles."));
        }

        var latest = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .OrderByDescending(e => e.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return Result.Success(new GetLatestOrganisationDataExportResponse(
                null, null, null, null, null, null, false));
        }

        var now = clock.UtcNowOffset();

        return Result.Success(new GetLatestOrganisationDataExportResponse(
            latest.Id,
            latest.Status,
            latest.RequestedAt,
            latest.CompletedAt,
            latest.ExpiresAt,
            latest.FileSizeBytes,
            latest.IsDownloadable(now)));
    }
}
