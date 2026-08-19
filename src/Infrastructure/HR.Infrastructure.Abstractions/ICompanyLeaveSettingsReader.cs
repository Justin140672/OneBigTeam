namespace HR.Infrastructure.Abstractions;

// Kept here (not moved to HR.Modules.Companies.Contracts) because CompanyLeaveSettings exposes
// WorkingPattern, which is owned by HR.Modules.Employees.Contracts. Moving this reader/DTO into
// Companies.Contracts would force Companies.Contracts to reference Employees.Contracts, breaking
// the "module Contracts projects reference only HR.SharedKernel" rule. Left as a
// cross-cutting contract in Infrastructure.Abstractions, same as IEmployeeDirectoryReader.
public interface ICompanyLeaveSettingsReader
{
    Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
