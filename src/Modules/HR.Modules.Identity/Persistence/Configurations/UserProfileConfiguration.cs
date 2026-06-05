using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.HasIndex(profile => profile.SupabaseAuthUserId)
            .IsUnique();

        builder.Property(profile => profile.SupabaseAuthUserId)
            .HasColumnName("supabase_auth_user_id")
            .IsRequired();

        builder.Property(profile => profile.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(profile => profile.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(profile => profile.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
