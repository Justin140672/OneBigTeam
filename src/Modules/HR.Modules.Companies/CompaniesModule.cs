using FluentValidation;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Companies;

public static class CompaniesModule
{
    public static IServiceCollection AddCompaniesModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddScoped<CreateCompanyHandler>();
        services.AddScoped<IValidator<CreateCompanyRequest>, CreateCompanyValidator>();

        services.AddDbContext<CompaniesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies")));

        return services;
    }

    public static async Task MigrateCompaniesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        await db.Database.MigrateAsync();
    }
}
