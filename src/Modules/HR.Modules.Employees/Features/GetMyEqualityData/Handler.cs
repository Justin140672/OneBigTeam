using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyEqualityData;

internal sealed class GetMyEqualityDataHandler(EmployeesDbContext db)
{
    public async Task<Result<GetMyEqualityDataResponse>> HandleAsync(
        GetMyEqualityDataRequest request,
        CancellationToken cancellationToken)
    {
        var record = await db.EmployeeEqualityData
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CompanyId == request.CompanyId && x.EmployeeId == request.EmployeeId,
                cancellationToken);

        return Result.Success(record is null
            ? EqualityDataResponseMapper.Empty()
            : EqualityDataResponseMapper.FromEntity(record));
    }
}
