using FluentValidation;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Employees;

public static class EmployeesModule
{
    public static IServiceCollection AddEmployeesModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<EmployeesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "employees")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateDepartmentHandler>();
        services.AddScoped<IValidator<CreateDepartmentRequest>, CreateDepartmentValidator>();

        services.AddScoped<CreatePositionProfileHandler>();
        services.AddScoped<IValidator<CreatePositionProfileRequest>, CreatePositionProfileValidator>();
    }

    public static async Task MigrateEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        await db.Database.MigrateAsync();
    }
}
