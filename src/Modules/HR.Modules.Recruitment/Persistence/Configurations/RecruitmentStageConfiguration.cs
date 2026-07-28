using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class RecruitmentStageConfiguration : IEntityTypeConfiguration<RecruitmentStage>
{
    public void Configure(EntityTypeBuilder<RecruitmentStage> builder)
    {
        builder.ToTable("recruitment_stages");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(s => s.IsTerminal)
            .HasColumnName("is_terminal")
            .IsRequired();

        builder.Property(s => s.TerminalOutcome)
            .HasColumnName("terminal_outcome")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(s => s.CompanyId);
        builder.HasIndex(s => new { s.CompanyId, s.Name }).IsUnique();
        builder.HasIndex(s => new { s.CompanyId, s.DisplayOrder }).IsUnique();
    }
}
