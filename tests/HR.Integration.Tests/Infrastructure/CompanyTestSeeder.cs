using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class CompanyTestSeeder
{
    public static async Task<Guid> CreateCompanyAsync(ApiWebApplicationFactory factory, string? name = null, Guid? companyId = null)
    {
        var resolvedCompanyId = companyId ?? Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await TestRoleSeeder.EnsureActiveSubscriptionAsync(scope, resolvedCompanyId);

        if (name is not null)
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var company = await companiesDb.Companies.SingleAsync(c => c.Id == resolvedCompanyId);
            company.Update(name, DateTimeOffset.UtcNow);
            await companiesDb.SaveChangesAsync();
        }

        return resolvedCompanyId;
    }
}
