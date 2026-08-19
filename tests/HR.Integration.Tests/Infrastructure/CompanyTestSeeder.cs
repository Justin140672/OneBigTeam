using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Provisions a Company (and active trial subscription) directly via <see cref="CompaniesDbContext"/>,
/// mirroring real production company state without going through HTTP.
///
/// Replaces the now-404 <c>POST /api/companies</c> call that many tests used purely as setup
/// boilerplate to obtain a <see cref="Guid"/> companyId before the dedicated CreateCompany
/// FastEndpoints slice (and its tests) were removed in commit 78a43344. This seeder reuses the
/// already-proven <see cref="TestRoleSeeder.EnsureActiveSubscriptionAsync"/> path.
/// </summary>
internal static class CompanyTestSeeder
{
    public static async Task<Guid> CreateCompanyAsync(ApiWebApplicationFactory factory, string? name = null)
    {
        var companyId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await TestRoleSeeder.EnsureActiveSubscriptionAsync(scope, companyId);

        if (name is not null)
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var company = await companiesDb.Companies.SingleAsync(c => c.Id == companyId);
            company.Update(name, DateTimeOffset.UtcNow);
            await companiesDb.SaveChangesAsync();
        }

        return companyId;
    }
}
