using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class OnboardingTemplateSeeder(EmployeesDbContext db)
{
    public async Task EnsureDefaultTemplateSeededAsync(Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadySeeded = await db.OnboardingTemplates
            .AsNoTracking()
            .AnyAsync(t => t.CompanyId == companyId, cancellationToken);

        if (alreadySeeded)
            return;

        var template = OnboardingTemplate.Create(
            Guid.NewGuid(), companyId,
            StandardOnboardingTemplateDefinition.Name,
            StandardOnboardingTemplateDefinition.Description,
            now, isDefault: true);

        foreach (var task in StandardOnboardingTemplateDefinition.Tasks)
        {
            template.AddTask(
                Guid.NewGuid(), task.Title, task.Description, task.Priority,
                task.AssignTo, task.DueDaysAfterStart, task.DisplayOrder, now);
        }

        db.OnboardingTemplates.Add(template);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            var seededConcurrently = await db.OnboardingTemplates
                .AsNoTracking()
                .AnyAsync(t => t.CompanyId == companyId, cancellationToken);

            if (!seededConcurrently)
                throw;
        }
    }
}
