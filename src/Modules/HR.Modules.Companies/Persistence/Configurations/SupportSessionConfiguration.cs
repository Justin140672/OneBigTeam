using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class SupportSessionConfiguration : IEntityTypeConfiguration<SupportSession>
{
    public void Configure(EntityTypeBuilder<SupportSession> builder)
    {
        builder.ToTable("support_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(s => s.IssuedByAdminUserId)
            .HasColumnName("issued_by_admin_user_id")
            .IsRequired();

        builder.Property(s => s.IssuedByAdminEmail)
            .HasColumnName("issued_by_admin_email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(s => s.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(s => s.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(s => s.RedeemedAt)
            .HasColumnName("redeemed_at");

        builder.Property(s => s.RevokedAt)
            .HasColumnName("revoked_at");

        builder.HasIndex(s => s.CompanyId)
            .HasDatabaseName("ix_support_sessions_company_id");

        builder.HasIndex(s => s.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_support_sessions_token_hash");
    }
}
