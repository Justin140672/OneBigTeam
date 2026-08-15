using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.TrialLengthDays)
            .HasColumnName("trial_length_days")
            .IsRequired()
            .HasDefaultValue(14);

        builder.Property(s => s.DefaultMonthlyPriceGbp)
            .HasColumnName("default_monthly_price_gbp")
            .HasPrecision(10, 2)
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(s => s.SupportEmail)
            .HasColumnName("support_email")
            .HasMaxLength(320)
            .IsRequired()
            .HasDefaultValue("support@hrplatform.com");

        builder.Property(s => s.MaintenanceModeEnabled)
            .HasColumnName("maintenance_mode_enabled")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.MaintenanceModeMessage)
            .HasColumnName("maintenance_mode_message")
            .HasMaxLength(2000);

        builder.Property(s => s.FeatureFlagsJson)
            .HasColumnName("feature_flags_json")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(s => s.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");
    }
}
