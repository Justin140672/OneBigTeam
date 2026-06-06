using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(r => r.NormalizedName)
            .IsUnique();

        builder.Property(r => r.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        var seedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        builder.HasData(
            Role.Create(SystemRoles.Employee,             "Employee",              seedDate),
            Role.Create(SystemRoles.Manager,              "Manager",               seedDate),
            Role.Create(SystemRoles.Recruiter,            "Recruiter",             seedDate),
            Role.Create(SystemRoles.HrAdministrator,      "HR Administrator",      seedDate),
            Role.Create(SystemRoles.Finance,              "Finance",               seedDate),
            Role.Create(SystemRoles.CompanyAdministrator, "Company Administrator", seedDate)
        );
    }
}
