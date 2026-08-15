using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

// Deliberate exception to the usual company_id-on-every-table rule: platform administrators are
// a platform-level/system concept with no company relationship at all (see 05-database-standards.md
// "Global/system tables may omit company_id").
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

        builder.HasIndex(a => a.Email).IsUnique();
    }
}
