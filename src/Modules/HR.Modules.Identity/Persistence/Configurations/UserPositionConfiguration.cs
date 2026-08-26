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

        // IAM-03: no FK constraint on UserId, matching UserRoleConfiguration's precedent (see its
        // remarks) — UserId here is the owning Employee's id (ApplicationUser.Id == EmployeeId by
        // convention, see AcceptInvite/SignUp), and by design a UserPosition row is written the
        // moment an employee is assigned to a position profile (Features/OnEmployeeCreated,
        // Features/OnEmployeePositionChanged), which routinely happens before that employee has
        // any ApplicationUser/UserProfile row at all (accounts are only created later via
        // InviteEmployeeUser -> AcceptInvite, or self-service SignUp). A hard FK to ApplicationUser
        // would reject every such write. An index still supports the lookups
        // IdentityAuthorizationService performs.
        builder.HasIndex(up => up.UserId);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(up => up.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
