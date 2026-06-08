using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Persistence;

internal sealed class EmployeesDbContext : DbContext
{
    public EmployeesDbContext(DbContextOptions<EmployeesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<PositionProfile> PositionProfiles => Set<PositionProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("employees");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmployeesDbContext).Assembly);
    }
}
