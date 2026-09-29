using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services.OnboardingTasks;

internal sealed class DownloadEmployeeImportTemplateTask(EmployeesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "download-employee-import-template";
    public string Name => "Download the Employee import template";
    public string Description => "Get the spreadsheet template to prepare your team's data before importing.";

    // Not mandatory — it's a helper step towards "Add your team" (ImportEmployeesTask), not a
    // distinct outcome of its own, so it's deliberately excluded from the completion percentage
    // to avoid double-counting the same underlying condition.
    public bool IsMandatory => false;
    public int Order => 4;

    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/data-import/employees/template/download");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var count = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId, cancellationToken);

        return count > 1;
    }
}
