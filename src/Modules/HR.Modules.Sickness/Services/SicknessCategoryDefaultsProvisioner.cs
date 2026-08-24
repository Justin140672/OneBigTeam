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
///
/// SICK-05: the previous default set ("Cold/Flu", "Back Pain", "Migraine") was diagnostic —
/// it named specific medical conditions, which the product spec
/// (specifications/product-specifications/15-sickness-management.md, "Sickness Categories")
/// explicitly says categories must not do. Replaced with the broad, non-diagnostic set the spec
/// defines. See specifications/product-specifications/00-current-product-decisions.md,
/// "Sickness management", for the confirmed decision record.
///
/// Non-destructive: this list only affects companies provisioned from this point forward (the
/// early-return above already skips any company that has categories). Existing companies keep
/// whatever categories they already have — including ones seeded under the old diagnostic
/// defaults — and this class must never be changed into a backfill/data migration that renames
/// or deletes previously-provisioned rows.
/// </summary>
internal sealed class SicknessCategoryDefaultsProvisioner(SicknessDbContext dbContext, IClock clock) : ISicknessCategoryDefaultsProvisioner
{
    public async Task EnsureDefaultSicknessCategoriesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.SicknessCategories.AnyAsync(c => c.CompanyId == companyId, cancellationToken))
            return;

        var now = clock.UtcNowOffset();

        dbContext.SicknessCategories.AddRange(
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Illness", 1, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Injury", 2, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Mental health", 3, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Medical appointment", 4, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Dependant care", 5, now),
            SicknessCategory.Create(Guid.NewGuid(), companyId, "Other", 6, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
