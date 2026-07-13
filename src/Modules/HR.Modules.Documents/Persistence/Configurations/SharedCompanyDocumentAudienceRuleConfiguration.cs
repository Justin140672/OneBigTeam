using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class SharedCompanyDocumentAudienceRuleConfiguration : IEntityTypeConfiguration<SharedCompanyDocumentAudienceRule>
{
    public void Configure(EntityTypeBuilder<SharedCompanyDocumentAudienceRule> builder)
    {
        builder.ToTable("shared_company_document_audience_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.SharedCompanyDocumentId)
            .HasColumnName("shared_company_document_id")
            .IsRequired();

        builder.Property(r => r.RuleType)
            .HasColumnName("rule_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.TargetId)
            .HasColumnName("target_id")
            .IsRequired();

        builder.HasOne<SharedCompanyDocument>()
            .WithMany()
            .HasForeignKey(r => r.SharedCompanyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => new { r.SharedCompanyDocumentId, r.RuleType, r.TargetId }).IsUnique();
    }
}
