using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class ApplicationStageHistoryEntryConfiguration : IEntityTypeConfiguration<ApplicationStageHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ApplicationStageHistoryEntry> builder)
    {
        builder.ToTable("application_stage_history_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.ApplicationId)
            .HasColumnName("application_id")
            .IsRequired();

        builder.Property(e => e.PreviousStage)
            .HasColumnName("previous_stage")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.NewStage)
            .HasColumnName("new_stage")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.ChangedByUserId)
            .HasColumnName("changed_by_user_id");

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(e => e.ChangedAt)
            .HasColumnName("changed_at")
            .IsRequired();

        builder.HasOne<Application>()
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.ApplicationId);
    }
}
