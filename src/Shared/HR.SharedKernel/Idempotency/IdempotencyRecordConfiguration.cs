using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Column mapping for a module's own <see cref="IIdempotencyRecord"/> entity. Each module applies
/// this explicitly for its own record type (e.g.
/// <c>modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration&lt;IdempotencyRecord&gt;())</c>)
/// since it lives outside the module's own assembly and won't be picked up by
/// <c>ApplyConfigurationsFromAssembly</c>.
///
/// Ticket 3 (P1) follow-up: the primary key is the composite (operation, company, actor, key) - see
/// <see cref="IIdempotencyRecord"/> for why a client-supplied key is never trusted alone.
/// </summary>
public sealed class IdempotencyRecordConfiguration<TRecord> : IEntityTypeConfiguration<TRecord>
    where TRecord : class, IIdempotencyRecord
{
    public void Configure(EntityTypeBuilder<TRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(r => new { r.OperationId, r.CompanyId, r.ActorId, r.Key });

        builder.Property(r => r.OperationId).HasColumnName("operation_id").HasMaxLength(200);
        builder.Property(r => r.CompanyId).HasColumnName("company_id");
        builder.Property(r => r.ActorId).HasColumnName("actor_id");
        builder.Property(r => r.Key).HasColumnName("key").HasMaxLength(200);
        builder.Property(r => r.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(128);
        builder.Property(r => r.ResponseStatusCode).HasColumnName("response_status_code");
        builder.Property(r => r.ResponseBodyJson).HasColumnName("response_body_json").HasColumnType("jsonb");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");

        // Ticket 3 (P1) follow-up item 6: the cleanup job scans for expired rows across all scopes,
        // so it needs an index that doesn't start with the (highly selective, cleanup-irrelevant)
        // primary key columns.
        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}
