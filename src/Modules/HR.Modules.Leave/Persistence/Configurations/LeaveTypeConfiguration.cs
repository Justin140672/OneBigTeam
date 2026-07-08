using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Code)
            .HasColumnName("code")
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(t => new { t.CompanyId, t.Code })
            .IsUnique();

        builder.Property(t => t.DefaultEntitlementDays)
            .HasColumnName("default_entitlement_days")
            .IsRequired();

        builder.Property(t => t.AccrualMethod)
            .HasColumnName("accrual_method")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Behaviour)
            .HasColumnName("behaviour")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.HasBalance)
            .HasColumnName("has_balance")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => t.CompanyId);
    }
}
