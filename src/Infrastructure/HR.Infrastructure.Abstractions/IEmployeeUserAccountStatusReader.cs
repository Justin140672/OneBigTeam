namespace HR.Infrastructure.Abstractions;

// Port used by the Employees module (EmployeeList "User Account" column, ticket #90) and any other
// module needing to display account/invitation status for a set of employees, without a direct
// module-to-module reference. Implemented in HR.Modules.Identity, which owns ApplicationUser and
// UserInvite. Employees not present in the returned dictionary should be treated as NoUser.
public interface IEmployeeUserAccountStatusReader
{
    Task<IReadOnlyDictionary<Guid, EmployeeUserAccountSummary>> GetStatusesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
