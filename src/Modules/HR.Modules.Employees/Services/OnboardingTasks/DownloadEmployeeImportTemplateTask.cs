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

    // HR.Web's template-download endpoint streams the file directly (Content-Disposition:
    // attachment) rather than navigating to a page — the "{companyId}" placeholder is substituted
    // by HR.Web with the current company id, same as every other task's link.
    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/data-import/employees/template/download");

    // There's no reliable signal that the file was actually opened/used — only that a real import
    // has since happened, which is the same completion condition ImportEmployeesTask (the next
    // task) already uses. Downloading the template is a means to that end, not a tracked action of
    // its own; not being mandatory, this never affects the checklist's completion percentage.
    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var count = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId, cancellationToken);

        return count > 1;
    }
}
