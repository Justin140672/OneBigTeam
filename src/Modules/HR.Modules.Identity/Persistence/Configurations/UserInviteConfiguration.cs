using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class UserInviteConfiguration : IEntityTypeConfiguration<UserInvite>
{
    public void Configure(EntityTypeBuilder<UserInvite> builder)
    {
        builder.ToTable("user_invites");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(i => i.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(i => i.Token).HasColumnName("token").HasMaxLength(64).IsRequired();
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(i => i.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(i => i.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(i => i.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(i => i.PendingRoleIds)
            .HasColumnName("pending_role_ids")
            .HasField("_pendingRoleIds")
            .UsePropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field)
            .HasConversion(
                v => string.Join(',', v),
                v => string.IsNullOrEmpty(v)
                    ? new List<Guid>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<Guid>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                v => v.ToList()));

        builder.HasIndex(i => i.Token).IsUnique();
        builder.HasIndex(i => i.EmployeeId);
    }
}
