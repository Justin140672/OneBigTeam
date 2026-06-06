using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class UserPositionConfiguration : IEntityTypeConfiguration<UserPosition>
{
    public void Configure(EntityTypeBuilder<UserPosition> builder)
    {
        builder.ToTable("user_positions");

        builder.HasKey(up => new { up.UserId, up.PositionId });

        builder.Property(up => up.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(up => up.PositionId)
            .HasColumnName("position_id")
            .IsRequired();

        builder.Property(up => up.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(up => up.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired(false);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(up => up.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
