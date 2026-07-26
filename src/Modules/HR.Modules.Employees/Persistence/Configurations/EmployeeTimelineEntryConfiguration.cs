using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeeTimelineEntryConfiguration : IEntityTypeConfiguration<EmployeeTimelineEntry>
{
    public void Configure(EntityTypeBuilder<EmployeeTimelineEntry> builder)
    {
        builder.ToTable("employee_timeline_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.EventDate)
            .HasColumnName("event_date")
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .IsRequired();

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .IsRequired();

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.PerformedByUserId)
            .HasColumnName("performed_by_user_id");

        builder.Property(e => e.SourceModule)
            .HasColumnName("source_module")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.SourceRecordId)
            .HasColumnName("source_record_id");

        builder.Property(e => e.Visibility)
            .HasColumnName("visibility")
            .IsRequired();

        builder.Property(e => e.CreatedDate)
            .HasColumnName("created_date")
            .IsRequired();

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => new { e.CompanyId, e.EmployeeId });

        // Dedup strategy: the natural key for "don't write the same source event twice" is
        // (company_id, source_module, event_type, source_record_id) — but SourceRecordId is
        // nullable (some event types, e.g. ManagerChanged, have no dedicated source entity to
        // point at). Postgres unique indexes treat NULLs as distinct from one another, so a single
        // unique index across a nullable column would silently fail to prevent duplicates for the
        // null case. Two partial/filtered unique indexes cover both cases instead:
        //   1. When source_record_id IS NOT NULL, dedup on the natural key above.
        //   2. When source_record_id IS NULL, dedup on (company_id, employee_id, event_type,
        //      event_date) instead, since that's the closest available substitute for a natural key.
        builder.HasIndex(e => new { e.CompanyId, e.SourceModule, e.EventType, e.SourceRecordId })
            .IsUnique()
            .HasFilter("source_record_id IS NOT NULL");

        builder.HasIndex(e => new { e.CompanyId, e.EmployeeId, e.EventType, e.EventDate })
            .IsUnique()
            .HasFilter("source_record_id IS NULL");
    }
}
