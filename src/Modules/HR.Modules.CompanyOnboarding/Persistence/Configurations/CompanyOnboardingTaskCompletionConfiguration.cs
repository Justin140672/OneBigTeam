using HR.Modules.CompanyOnboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.CompanyOnboarding.Persistence.Configurations;

internal sealed class CompanyOnboardingTaskCompletionConfiguration : IEntityTypeConfiguration<CompanyOnboardingTaskCompletion>
{
    public void Configure(EntityTypeBuilder<CompanyOnboardingTaskCompletion> builder)
    {
        builder.ToTable("task_completions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(t => t.TaskKey)
            .HasColumnName("task_key")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.IsCompleted)
            .HasColumnName("is_completed")
            .IsRequired();

        builder.Property(t => t.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(t => t.CompanyId);

        builder.HasIndex(t => new { t.CompanyId, t.TaskKey })
            .IsUnique();
    }
}
