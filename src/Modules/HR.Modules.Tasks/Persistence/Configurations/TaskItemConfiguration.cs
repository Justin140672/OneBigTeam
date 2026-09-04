using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Tasks.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("task_items");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.ActionType)
            .HasColumnName("action_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.DueDate)
            .HasColumnName("due_date");

        builder.Property(t => t.AssignedEmployeeId)
            .HasColumnName("assigned_employee_id");

        builder.Property(t => t.AssignedUserId)
            .HasColumnName("assigned_user_id");

        builder.Property(t => t.SourceEntityId)
            .HasColumnName("source_entity_id");

        builder.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(200);

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(t => t.CompletedBy)
            .HasColumnName("completed_by");

        builder.Property(t => t.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => t.CompanyId);
        builder.HasIndex(t => t.AssignedEmployeeId);
        builder.HasIndex(t => t.AssignedUserId);
        builder.HasIndex(t => new { t.CompanyId, t.Status });

        // OBT-REM-13: DB-enforced idempotency for workflow-created tasks. Scoped per company (not
        // globally) and filtered to non-null keys only, so the many tasks with no idempotency key
        // (the overwhelming majority — manual/interactive-endpoint-created tasks) never collide with
        // each other, and the same key in two different companies never collides either. Callers that
        // want idempotent creation supply a deterministic key such as
        // "SicknessEvidenceOverdue:{evidenceRequestId}" — different workflow keys against the same
        // source entity (e.g. a different workflow prefix) remain free to create separate tasks.
        builder.HasIndex(t => new { t.CompanyId, t.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ix_task_items_company_id_idempotency_key");
    }
}
