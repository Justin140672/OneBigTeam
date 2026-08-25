using HR.Modules.Offboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Offboarding.Persistence.Configurations;

internal sealed class OffboardingTaskConfiguration : IEntityTypeConfiguration<OffboardingTask>
{
    public void Configure(EntityTypeBuilder<OffboardingTask> builder)
    {
        builder.ToTable("offboarding_tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.OffboardingPlanId)
            .HasColumnName("offboarding_plan_id")
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(t => t.AssignTo)
            .HasColumnName("assign_to")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.AssignedEmployeeId)
            .HasColumnName("assigned_employee_id");

        builder.Property(t => t.AssetAssignmentId)
            .HasColumnName("asset_assignment_id");

        builder.Property(t => t.RequiresHrConfirmation)
            .HasColumnName("requires_hr_confirmation")
            .IsRequired();

        builder.Property(t => t.IsMandatory)
            .HasColumnName("is_mandatory")
            .IsRequired();

        builder.Property(t => t.SkipReason)
            .HasColumnName("skip_reason")
            .HasMaxLength(1000);

        builder.Property(t => t.SkippedByUserId)
            .HasColumnName("skipped_by_user_id");

        builder.Property(t => t.SkippedAt)
            .HasColumnName("skipped_at");

        builder.Property(t => t.DueDate)
            .HasColumnName("due_date");

        builder.Property(t => t.TaskItemCreatedAt)
            .HasColumnName("task_item_created_at");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<OffboardingPlan>()
            .WithMany()
            .HasForeignKey(t => t.OffboardingPlanId);

        builder.HasIndex(t => t.OffboardingPlanId);
        builder.HasIndex(t => new { t.CompanyId, t.OffboardingPlanId });

        // OFF-03: lets OffboardingTaskSynchronizer / OffboardingPlanCreationReconciliationJob
        // cheaply find every task still awaiting its Tasks-module TaskItem, without a full table
        // scan, across every plan/company.
        builder.HasIndex(t => t.TaskItemCreatedAt)
            .HasDatabaseName("ix_offboarding_tasks_task_item_created_at");

        // OFF-04: lets the reconciliation job cheaply check which of a plan's currently-assigned
        // assets already have an OffboardingTask, without a full table scan.
        builder.HasIndex(t => t.AssetAssignmentId)
            .HasDatabaseName("ix_offboarding_tasks_asset_assignment_id");
    }
}
