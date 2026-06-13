using HR.Modules.Leave.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Leave.Persistence.Configurations;

internal sealed class ToilTransactionConfiguration : IEntityTypeConfiguration<ToilTransaction>
{
    public void Configure(EntityTypeBuilder<ToilTransaction> builder)
    {
        builder.ToTable("toil_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(t => t.LeaveBalanceId)
            .HasColumnName("leave_balance_id")
            .IsRequired();

        builder.Property(t => t.Days)
            .HasColumnName("days")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        builder.Property(t => t.OccurredOn)
            .HasColumnName("occurred_on")
            .IsRequired();

        builder.Property(t => t.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => new { t.CompanyId, t.EmployeeId });
        builder.HasIndex(t => new { t.CompanyId, t.LeaveBalanceId });
        builder.HasIndex(t => new { t.CompanyId, t.EmployeeId, t.OccurredOn });
    }
}
