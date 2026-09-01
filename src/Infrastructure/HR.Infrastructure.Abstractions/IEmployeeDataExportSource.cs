namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface used by the organisation data export job to obtain all
/// Employees-module data for a company (employees, departments, position profiles,
/// compensation history, emergency contacts, employment history). Implemented by an internal
/// service in HR.Modules.Employees and DI-registered in EmployeesModule. Must enforce company_id.
/// </summary>
public interface IEmployeeDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
