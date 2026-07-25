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
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<PositionProfileRequiredDocument> PositionProfileRequiredDocuments => Set<PositionProfileRequiredDocument>();
    public DbSet<PositionProfileRequiredAsset> PositionProfileRequiredAssets => Set<PositionProfileRequiredAsset>();
    public DbSet<PositionProfileOnboardingTemplate> PositionProfileOnboardingTemplates => Set<PositionProfileOnboardingTemplate>();
    public DbSet<EmploymentType> EmploymentTypes => Set<EmploymentType>();
    public DbSet<Compensation> Compensations => Set<Compensation>();
    public DbSet<OnboardingTemplate> OnboardingTemplates => Set<OnboardingTemplate>();
    public DbSet<OnboardingTemplateTask> OnboardingTemplateTasks => Set<OnboardingTemplateTask>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EmployeeLeavingProcess> EmployeeLeavingProcesses => Set<EmployeeLeavingProcess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("employees");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmployeesDbContext).Assembly);
    }
}
