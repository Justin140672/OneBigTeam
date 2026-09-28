using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetCustomerDatabaseAssignment;

internal sealed class GetCustomerDatabaseAssignmentHandler(
    PlatformDbContext platformDb)
{
    public async Task<Result<GetCustomerDatabaseAssignmentResponse>> HandleAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        // Query PlatformDbContext.CustomerDatabaseAssignments by companyId
        var assignment = await platformDb.CustomerDatabaseAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId, cancellationToken);

        if (assignment is null)
        {
            return Result.Failure<GetCustomerDatabaseAssignmentResponse>(
                Error.NotFound($"No database assignment found for company {companyId}."));
        }

        var response = new GetCustomerDatabaseAssignmentResponse(
            assignment.CompanyId,
            assignment.Status,
            assignment.DatabaseKey,
            assignment.SchemaOid,
            assignment.CreatedAt,
            assignment.UpdatedAt);

        return Result.Success(response);
    }
}
