using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// Implements ISicknessCategoryDefaultsProvisioner — see the interface doc comment in
/// HR.Infrastructure.Abstractions for why this exists. Mirrors SicknessModule's dev seed set so
/// production provisioning never drifts out of sync with what the dev/E2E environment already
/// treats as "correct".
/// </summary>
internal sealed class SicknessCategoryDefaultsProvisioner(SicknessDbContext dbContext, IClock clock) : ISicknessCategoryDefaultsProvisioner
{
    public async Task EnsureDefaultSicknessCategoriesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.SicknessCategories.AnyAsync(c => c.CompanyId == companyId, cancellationToken))
            return;

        var now = clock.UtcNowOffset();

        dbContext.SicknessCategories.AddRange(
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold/Flu", 1, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Back Pain", 2, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Migraine", 3, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
