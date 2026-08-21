using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services.OnboardingTasks;

internal sealed class CompleteEmployeeRecordTask(EmployeesDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "complete-employee-record";
    public string Name => "Update your employee record";
    public string Description => "Add your personal details to finish setting up your own employee record.";
    public bool IsMandatory => true;
    public int Order => 0; // Must appear first in the checklist — lower than every other
    // IOnboardingTaskDefinition's Order across modules (lowest existing is 1, see
    // CompleteCompanyDetailsTask), since completing your own placeholder employee record on first
    // login is a prerequisite to everything else in the Getting Started checklist.

    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/getting-started");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var anyIncomplete = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.CompanyId == companyId && e.RequiresInitialSetup, cancellationToken);
        return !anyIncomplete;
    }
}
