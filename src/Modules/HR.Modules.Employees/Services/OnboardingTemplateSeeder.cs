using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Seeds a single reasonable default Onboarding Template ("Standard Onboarding") with a starter
/// checklist the first time a company's onboarding template list is requested, mirroring the
/// per-company idempotent lazy-seeding pattern used by
/// HR.Modules.Recruitment.Services.RecruitmentStageSeeder for default recruitment stages. A no-op
/// once a company already has at least one OnboardingTemplate row, so this is always safe to call
/// unconditionally. Templates remain fully editable/deactivatable afterward via the existing
/// CRUD features.
/// </summary>
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
            Guid.NewGuid(), companyId, "Standard Onboarding",
            "Default new-starter checklist covering the essentials for a new hire's first two weeks.", now);

        template.AddTask(Guid.NewGuid(), "Send welcome email", "Introduce the company, share first-day logistics.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 1, now);
        template.AddTask(Guid.NewGuid(), "Prepare workstation and equipment", "Laptop, accounts, desk setup ready before day one.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 2, now);
        template.AddTask(Guid.NewGuid(), "Complete right-to-work checks", "Verify and file required employment documentation.", TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Manager, 1, 3, now);
        template.AddTask(Guid.NewGuid(), "Company induction session", "Overview of policies, values, and company structure.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 3, 4, now);
        template.AddTask(Guid.NewGuid(), "Meet the team", "Introductions with immediate team and key stakeholders.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 3, 5, now);
        template.AddTask(Guid.NewGuid(), "Set 30-day goals", "Agree initial objectives and success measures.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 14, 6, now);
        template.AddTask(Guid.NewGuid(), "Complete mandatory training", "Health & safety, compliance and any role-specific training.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 14, 7, now);

        db.OnboardingTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
    }
}
