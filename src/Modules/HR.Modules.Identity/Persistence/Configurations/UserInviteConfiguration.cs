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
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(i => i.Token).IsUnique();
        builder.HasIndex(i => i.EmployeeId);
    }
}
