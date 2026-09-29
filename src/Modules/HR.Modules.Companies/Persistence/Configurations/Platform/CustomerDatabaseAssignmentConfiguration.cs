using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations.Platform;

internal sealed class CustomerDatabaseAssignmentConfiguration : IEntityTypeConfiguration<CustomerDatabaseAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerDatabaseAssignment> builder)
    {
        builder.ToTable("customer_database_assignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.DatabaseKey)
            .HasColumnName("database_key")
            .HasMaxLength(100);

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.SchemaOid)
            .HasColumnName("schema_oid");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(a => a.CompanyId)
            .HasDatabaseName("ix_customer_database_assignments_company_id");

        builder.HasIndex(a => a.DatabaseKey)
            .HasDatabaseName("ix_customer_database_assignments_database_key");

        builder.HasIndex(a => new { a.Status, a.CompanyId })
            .HasDatabaseName("ix_customer_database_assignments_status_company_id");
    }
}
