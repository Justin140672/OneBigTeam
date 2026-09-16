using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class CandidatePurgeAuditDeliveryConfiguration : IEntityTypeConfiguration<CandidatePurgeAuditDelivery>
{
    public void Configure(EntityTypeBuilder<CandidatePurgeAuditDelivery> builder)
    {
        builder.ToTable("candidate_purge_audit_deliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(d => d.PurgedBy).HasColumnName("purged_by").IsRequired();
        builder.Property(d => d.CandidateIdsJson).HasColumnName("candidate_ids_json").HasColumnType("text").IsRequired();
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(d => d.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.DeliveredAt).HasColumnName("delivered_at");

        builder.HasIndex(d => new { d.CompanyId, d.Status });
    }
}
