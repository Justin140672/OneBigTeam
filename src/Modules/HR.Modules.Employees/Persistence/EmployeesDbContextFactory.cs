using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Employees.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// Set the EMPLOYEES_CONNECTION_STRING environment variable or update the fallback
/// to point at your local Postgres instance before running migrations.
/// </summary>
internal sealed class EmployeesDbContextFactory : IDesignTimeDbContextFactory<EmployeesDbContext>
{
    public EmployeesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EMPLOYEES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<EmployeesDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "employees"));

        return new EmployeesDbContext(optionsBuilder.Options);
    }
}
