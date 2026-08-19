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
    public int Order => 5;

    // HR.Web's employee import wizard route is company-scoped
    // ("/companies/{CompanyId:guid}/data-import/employees") — the "{companyId}" placeholder is
    // substituted by HR.Web with the current company id. Points at the import wizard rather than
    // the plain employee list, since "Add your team" is specifically about getting employees in,
    // not just browsing the (still-empty) list.
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
