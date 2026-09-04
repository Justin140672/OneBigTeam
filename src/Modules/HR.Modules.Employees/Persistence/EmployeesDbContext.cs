using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HR.Modules.Employees.Persistence;

internal sealed class EmployeesDbContext : DbContext
{
    private readonly ISensitiveDataProtector? _protector;

    public EmployeesDbContext(
        DbContextOptions<EmployeesDbContext> options,
        ISensitiveDataProtector? protector = null)
        : base(options)
    {
        _protector = protector;
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
    public DbSet<EmployeeNote> EmployeeNotes => Set<EmployeeNote>();
    public DbSet<EmployeePromotion> EmployeePromotions => Set<EmployeePromotion>();
    public DbSet<EmployeeTimelineEntry> EmployeeTimelineEntries => Set<EmployeeTimelineEntry>();
    public DbSet<EmployeeEqualityData> EmployeeEqualityData => Set<EmployeeEqualityData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("employees");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmployeesDbContext).Assembly);

        var protector = _protector ?? ResolveProtector();
        if (protector is null)
            return;

        var converter = new ValueConverter<string, string>(
            plaintext => protector.Protect(plaintext),
            stored => Decrypt(protector, stored));

        var entity = modelBuilder.Entity<EmployeeEqualityData>();
        // The converter only ever runs for non-null values (EF skips nulls), so the
        // non-nullable <string,string> converter is safe on these nullable properties.
#pragma warning disable CS8620
        entity.Property(x => x.GenderIdentity).HasConversion(converter);
        entity.Property(x => x.GenderIdentitySelfDescribed).HasConversion(converter);
        entity.Property(x => x.MarriedOrCivilPartnershipStatus).HasConversion(converter);
        entity.Property(x => x.EthnicGroup).HasConversion(converter);
        entity.Property(x => x.EthnicGroupSelfDescribed).HasConversion(converter);
        entity.Property(x => x.DisabilityStatus).HasConversion(converter);
        entity.Property(x => x.DisabilityImpact).HasConversion(converter);
        entity.Property(x => x.SexualOrientation).HasConversion(converter);
        entity.Property(x => x.SexualOrientationSelfDescribed).HasConversion(converter);
        entity.Property(x => x.ReligionOrBelief).HasConversion(converter);
        entity.Property(x => x.ReligionOrBeliefSelfDescribed).HasConversion(converter);
#pragma warning restore CS8620
    }

    // Tolerates not-yet-encrypted plaintext during the field roll-out.
    private static string Decrypt(ISensitiveDataProtector protector, string stored)
        => protector.TryUnprotect(stored, out var plaintext) ? plaintext! : stored;

    private ISensitiveDataProtector? ResolveProtector()
    {
        try
        {
            return this.GetService<ISensitiveDataProtector>();
        }
        catch
        {
            return null;
        }
    }
}
