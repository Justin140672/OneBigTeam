using HR.Modules.Offboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Offboarding.Persistence.Configurations;

internal sealed class OffboardingPlanConfiguration : IEntityTypeConfiguration<OffboardingPlan>
{
    public void Configure(EntityTypeBuilder<OffboardingPlan> builder)
    {
        builder.ToTable("offboarding_plans");

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

        builder.Property(p => p.LastWorkingDay)
            .HasColumnName("last_working_day")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(p => p.IsBackdated)
            .HasColumnName("is_backdated")
            .IsRequired();

        builder.Property(p => p.RequiresHrReconciliation)
            .HasColumnName("requires_hr_reconciliation")
            .IsRequired();

        builder.Property(p => p.HasIncompleteOffboardingAtDeparture)
            .HasColumnName("has_incomplete_offboarding_at_departure")
            .IsRequired();

        builder.Property(p => p.FinalReviewTaskCreatedAt)
            .HasColumnName("final_review_task_created_at");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(p => p.CompanyId);
        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId });
        builder.HasIndex(p => new { p.CompanyId, p.Status });

        // OFF-05: lets HR list/query "needs reconciliation" plans cheaply, company-wide.
        builder.HasIndex(p => new { p.CompanyId, p.RequiresHrReconciliation })
            .HasDatabaseName("ix_offboarding_plans_company_id_requires_hr_reconciliation");

        // OFF-07: lets HR list/query the "incomplete offboarding at departure" exception queue
        // cheaply, company-wide — mirrors the RequiresHrReconciliation index above.
        builder.HasIndex(p => new { p.CompanyId, p.HasIncompleteOffboardingAtDeparture })
            .HasDatabaseName("ix_offboarding_plans_company_id_incomplete_at_departure");

        // OFF-03: database-level backstop against duplicate active plans for the same employee.
        // "Active" here means anything not yet in a terminal state (Completed/Cancelled) — an
        // employee may accumulate any number of Completed/Cancelled historical plans, but never
        // more than one NotStarted/InProgress plan at a time. This is what makes concurrent/repeated
        // "start offboarding" attempts (manual or automatic) safe: the in-memory pre-check in
        // StartOffboardingHandler is only a fast-path optimisation, this index is the real guarantee
        // under concurrency, and a violation is caught there and turned into a Conflict result.
        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId })
            .IsUnique()
            .HasFilter("status NOT IN ('Completed', 'Cancelled')")
            .HasDatabaseName("ix_offboarding_plans_company_id_employee_id_active");
    }
}
