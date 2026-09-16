using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class InviteAcceptanceOperationConfiguration : IEntityTypeConfiguration<InviteAcceptanceOperation>
{
    public void Configure(EntityTypeBuilder<InviteAcceptanceOperation> builder)
    {
        builder.ToTable("invite_acceptance_operations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.InviteId).HasColumnName("invite_id").IsRequired();
        builder.HasIndex(o => o.InviteId).IsUnique();

        builder.Property(o => o.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(o => o.EmployeeId).HasColumnName("employee_id").IsRequired();

        builder.Property(o => o.Email).HasColumnName("email").HasMaxLength(256).IsRequired();

        builder.Property(o => o.SupabaseAuthUserId).HasColumnName("supabase_auth_user_id");

        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(32).IsRequired();

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(o => o.CompletedAt).HasColumnName("completed_at");
    }
}
