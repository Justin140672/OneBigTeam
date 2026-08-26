using HR.Modules.Employees.Contracts;

namespace HR.Modules.Identity.Authorization;

internal sealed class TargetUserCompanyGuard(IEmployeeAudienceReader employeeAudienceReader) : ITargetUserCompanyGuard
{
    public Task<bool> IsMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        employeeAudienceReader.EmployeeExistsAsync(companyId, userId, cancellationToken);
}
