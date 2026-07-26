using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeePromotionConfiguration : IEntityTypeConfiguration<EmployeePromotion>
{
    public void Configure(EntityTypeBuilder<EmployeePromotion> builder)
    {
        builder.ToTable("employee_promotions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(p => p.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.PreviousPositionProfileId)
            .HasColumnName("previous_position_profile_id")
            .IsRequired();

        builder.HasOne<PositionProfile>()
            .WithMany()
            .HasForeignKey(p => p.PreviousPositionProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.NewPositionProfileId)
            .HasColumnName("new_position_profile_id")
            .IsRequired();

        builder.HasOne<PositionProfile>()
            .WithMany()
            .HasForeignKey(p => p.NewPositionProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.NewManagerId)
            .HasColumnName("new_manager_id");

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.NewManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.NewLocationId)
            .HasColumnName("new_location_id");

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(p => p.NewLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.EffectiveDate)
            .HasColumnName("effective_date")
            .IsRequired();

        builder.Property(p => p.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(4000);

        builder.Property(p => p.CompensationId)
            .HasColumnName("compensation_id");

        builder.HasOne<Compensation>()
            .WithMany()
            .HasForeignKey(p => p.CompensationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(p => p.CreatedDate)
            .HasColumnName("created_date")
            .IsRequired();

        builder.Property(p => p.CompletedAt)
            .HasColumnName("completed_at");

        builder.HasIndex(p => p.CompanyId);
        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId });
    }
}
