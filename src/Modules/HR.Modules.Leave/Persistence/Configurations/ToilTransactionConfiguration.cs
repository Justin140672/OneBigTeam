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

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ToilTransactionType.Earned);

        builder.Property(t => t.Days)
            .HasColumnName("days")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        builder.Property(t => t.OccurredOn)
            .HasColumnName("occurred_on")
            .IsRequired();

        builder.Property(t => t.ExpiresOn)
            .HasColumnName("expires_on");

        builder.Property(t => t.RelatedTransactionId)
            .HasColumnName("related_transaction_id");

        builder.Property(t => t.ReversesTransactionId)
            .HasColumnName("reverses_transaction_id");

        builder.Property(t => t.SourceLeaveRequestId)
            .HasColumnName("source_leave_request_id");

        builder.Property(t => t.ActorEmployeeId)
            .HasColumnName("actor_employee_id")
            .IsRequired();

        builder.Property(t => t.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => new { t.CompanyId, t.EmployeeId });
        builder.HasIndex(t => new { t.CompanyId, t.LeaveBalanceId });
        builder.HasIndex(t => new { t.CompanyId, t.EmployeeId, t.OccurredOn });
        builder.HasIndex(t => t.RelatedTransactionId);
        builder.HasIndex(t => t.SourceLeaveRequestId);
    }
}
