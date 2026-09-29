using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class PlatformAdministratorConfiguration : IEntityTypeConfiguration<PlatformAdministrator>
{
    public void Configure(EntityTypeBuilder<PlatformAdministrator> builder)
    {
        builder.ToTable("platform_administrators");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(a => a.SupabaseAuthUserId).HasColumnName("supabase_auth_user_id");
        builder.Property(a => a.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(a => a.DisabledAt).HasColumnName("disabled_at");
        builder.Property(a => a.DisabledByUserId).HasColumnName("disabled_by_user_id");

        builder.Property(a => a.ProvisioningStatus)
            .HasColumnName("provisioning_status")
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(Domain.PlatformAdministratorProvisioningStatus.Active);

        builder.Property(a => a.ProvisioningCorrelationId).HasColumnName("provisioning_correlation_id");
        builder.Property(a => a.IsNewIdentityProviderAccount)
            .HasColumnName("is_new_identity_provider_account")
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(a => a.ProvisioningFailureReason).HasColumnName("provisioning_failure_reason").HasMaxLength(500);
        builder.Property(a => a.ProvisioningStartedAt).HasColumnName("provisioning_started_at");
        builder.Property(a => a.ProvisioningCompletedAt).HasColumnName("provisioning_completed_at");

        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(1);

        builder.HasIndex(a => a.Email).IsUnique();

        builder.HasIndex(a => a.SupabaseAuthUserId)
            .IsUnique()
            .HasFilter("supabase_auth_user_id IS NOT NULL")
            .HasDatabaseName("ix_platform_administrators_supabase_auth_user_id_unique");
    }
}
