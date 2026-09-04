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
        builder.Property(x => x.CaringResponsibilities).HasColumnName("caring_responsibilities").HasColumnType("text");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.EmployeeId }).IsUnique();

        // Ticket 8 — equality data follows the employee lifecycle and must never survive as an
        // identifiable orphan. A real FK to employees.employees guarantees a row cannot exist
        // without its employee, and ON DELETE CASCADE guarantees it is destroyed the instant the
        // employee row is physically deleted (the manual per-store customer-deletion procedure in
        // docs/compliance/data-protection-operations.md, and full-tenant deletion which drops the
        // whole `employees` schema).
        //
        // Cascade is deliberately chosen here even though sibling employee-child tables use
        // DeleteBehavior.Restrict: those rows carry independent business/retention value and
        // employees are only ever *soft*-deleted (Status = FormerEmployee) in normal operation, so
        // this cascade never fires during offboarding/leaving — only on a genuine physical DELETE,
        // which is exactly the moment this special-category record must disappear.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .HasPrincipalKey(e => e.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
