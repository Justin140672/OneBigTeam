namespace HR.Modules.Employees.Features.SearchEmployeeDirectory;

internal sealed record SearchEmployeeDirectoryRequest(
    Guid CompanyId,
    string? Term,
    bool IncludeLeavers = false,
    int Limit = 20);
