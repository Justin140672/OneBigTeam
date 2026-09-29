using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

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
