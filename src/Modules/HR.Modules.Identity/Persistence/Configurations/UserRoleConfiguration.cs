using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.Property(ur => ur.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ur => ur.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(ur => ur.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        // No FK constraint on UserId: this column can reference either ApplicationUser.Id
        // (local-auth path — AcceptInvite, DevAuthHandler, seeded dev personas) or
        // UserProfile.Id (real Supabase-backed users, created by self-service SignUp as of
        // Phase B) — see SignUpHandler.CreateIdentityRecordAsync's remarks on why UserRole.UserId
        // must equal UserProfile.Id, not the raw Supabase auth user id. A single FK to one table
        // would incorrectly reject role rows for the other table's users.
        builder.HasIndex(ur => ur.UserId);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
