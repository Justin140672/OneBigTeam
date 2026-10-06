using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class SignUpOperationConfiguration : IEntityTypeConfiguration<SignUpOperation>
{
    public void Configure(EntityTypeBuilder<SignUpOperation> builder)
    {
        builder.ToTable("signup_operations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ix_signup_operations_idempotency_key");

        builder.Property(o => o.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(128);
        builder.Property(o => o.AdminEmail).HasColumnName("admin_email").HasMaxLength(256).IsRequired();
        builder.Property(o => o.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256).IsRequired();
        builder.HasIndex(o => o.NormalizedEmail)
            .IsUnique()
            .HasFilter("status = 'in_progress'")
            .HasDatabaseName("ix_signup_operations_active_email");

        builder.Property(o => o.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(o => o.EmployeeId).HasColumnName("employee_id");
        builder.Property(o => o.SupabaseAuthUserId).HasColumnName("supabase_auth_user_id");

        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(o => o.Stage).HasColumnName("stage").HasMaxLength(32).IsRequired();
        builder.Property(o => o.ResponseJson).HasColumnName("response_json");
        builder.Property(o => o.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(o => o.FailureMessage).HasColumnName("failure_message").HasMaxLength(512);
        builder.Property(o => o.LastError).HasColumnName("last_error").HasMaxLength(512);
        builder.Property(o => o.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(o => o.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(o => o.CompletedAt).HasColumnName("completed_at");

        builder.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(o => new { o.Status, o.UpdatedAt }).HasDatabaseName("ix_signup_operations_status_updated_at");
    }
}
