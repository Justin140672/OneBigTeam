using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmployeeImportLookupReader"/>: lets tests seed which employee
/// numbers/work emails "already exist" and which references resolve to a real employee id,
/// without needing a live EmployeesDbContext.
/// </summary>
internal sealed class FakeEmployeeImportLookupReader : IEmployeeImportLookupReader
{
    private readonly HashSet<string> _existingEmployeeNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existingWorkEmails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _referenceToEmployeeId = new(StringComparer.OrdinalIgnoreCase);

    public void SeedExistingEmployeeNumber(string employeeNumber) =>
        _existingEmployeeNumbers.Add(employeeNumber.Trim());

    public void SeedExistingWorkEmail(string workEmail) =>
        _existingWorkEmails.Add(workEmail.Trim());

    public void SeedReference(string reference, Guid employeeId) =>
        _referenceToEmployeeId[reference.Trim()] = employeeId;

    public Task<bool> EmployeeNumberExistsAsync(Guid companyId, string employeeNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_existingEmployeeNumbers.Contains(employeeNumber.Trim()));

    public Task<bool> WorkEmailExistsAsync(Guid companyId, string workEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_existingWorkEmails.Contains(workEmail.Trim()));

    public Task<Guid?> FindEmployeeIdByReferenceAsync(Guid companyId, string reference, CancellationToken cancellationToken) =>
        Task.FromResult(_referenceToEmployeeId.TryGetValue(reference.Trim(), out var id) ? (Guid?)id : null);
}
