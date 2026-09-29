using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services.OnboardingTasks;

internal sealed class ImportEmployeesTask(EmployeesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "import-employees";
    public string Name => "Add your team";
    public string Description => "Add employees individually or import your team from a spreadsheet.";
    public bool IsMandatory => true;
    public int Order => 5;

    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/data-import/employees");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var count = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId, cancellationToken);

        return count > 1;
    }
}
