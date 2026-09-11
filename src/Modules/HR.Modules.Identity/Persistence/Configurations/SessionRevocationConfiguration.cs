using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

// Deliberate exception to the usual company_id-on-every-table rule: this table is a system/security
// record keyed by the identity provider's own user id, not tenant-owned business data (mirrors
// PlatformAdministratorConfiguration's same documented exception — see
// 05-database-standards.md "Global/system tables may omit company_id"). A user's company is not
// even reliably known at the point a revocation is recorded (platform administrators have none).
internal sealed class SessionRevocationConfiguration : IEntityTypeConfiguration<SessionRevocation>
{
    public void Configure(EntityTypeBuilder<SessionRevocation> builder)
    {
        builder.ToTable("session_revocations");

        builder.HasKey(r => r.SupabaseAuthUserId);
        builder.Property(r => r.SupabaseAuthUserId).HasColumnName("supabase_auth_user_id").ValueGeneratedNever();
        builder.Property(r => r.RevokedAt).HasColumnName("revoked_at").IsRequired();
    }
}
