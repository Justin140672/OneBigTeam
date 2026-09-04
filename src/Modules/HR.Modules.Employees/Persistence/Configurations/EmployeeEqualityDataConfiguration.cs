using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeeEqualityDataConfiguration : IEntityTypeConfiguration<EmployeeEqualityData>
{
    public void Configure(EntityTypeBuilder<EmployeeEqualityData> builder)
    {
        builder.ToTable("employee_equality_data");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(x => x.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        // Special-category data — stored as text (ciphertext is longer than plaintext, no length cap).
        builder.Property(x => x.GenderIdentity).HasColumnName("gender_identity").HasColumnType("text");
        builder.Property(x => x.GenderIdentitySelfDescribed).HasColumnName("gender_identity_self_described").HasColumnType("text");
        builder.Property(x => x.MarriedOrCivilPartnershipStatus).HasColumnName("married_or_civil_partnership_status").HasColumnType("text");
        builder.Property(x => x.EthnicGroup).HasColumnName("ethnic_group").HasColumnType("text");
        builder.Property(x => x.EthnicGroupSelfDescribed).HasColumnName("ethnic_group_self_described").HasColumnType("text");
        builder.Property(x => x.DisabilityStatus).HasColumnName("disability_status").HasColumnType("text");
        builder.Property(x => x.DisabilityImpact).HasColumnName("disability_impact").HasColumnType("text");
        builder.Property(x => x.SexualOrientation).HasColumnName("sexual_orientation").HasColumnType("text");
        builder.Property(x => x.SexualOrientationSelfDescribed).HasColumnName("sexual_orientation_self_described").HasColumnType("text");
        builder.Property(x => x.ReligionOrBelief).HasColumnName("religion_or_belief").HasColumnType("text");
        builder.Property(x => x.ReligionOrBeliefSelfDescribed).HasColumnName("religion_or_belief_self_described").HasColumnType("text");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.EmployeeId }).IsUnique();
    }
}
