using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services.OnboardingTasks;

internal sealed class ImportEmployeesTask(EmployeesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "import-employees";
    public string Name => "Add your team";
    public string Description => "Import or add your employees to get started.";
    public bool IsMandatory => true;
    // HR.Web's employee list route is company-scoped ("/companies/{CompanyId:guid}/employees") —
    // the "{companyId}" placeholder is substituted by HR.Web with the current company id.
    public string LinkUrl => "/companies/{companyId}/employees";
    public int Order => 4;

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var count = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId, cancellationToken);

        return count > 1;
    }
}
