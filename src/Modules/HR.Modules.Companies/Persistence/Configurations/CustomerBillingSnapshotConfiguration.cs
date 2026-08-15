using HR.Modules.Companies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Companies.Persistence.Configurations;

internal sealed class CustomerBillingSnapshotConfiguration : IEntityTypeConfiguration<CustomerBillingSnapshot>
{
    public void Configure(EntityTypeBuilder<CustomerBillingSnapshot> builder)
    {
        builder.ToTable("customer_billing_snapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(s => s.ComputedAt)
            .HasColumnName("computed_at")
            .IsRequired();

        builder.Property(s => s.ActiveEmployees)
            .HasColumnName("active_employees")
            .IsRequired();

        builder.Property(s => s.FutureStarters)
            .HasColumnName("future_starters")
            .IsRequired();

        builder.Property(s => s.Leavers)
            .HasColumnName("leavers")
            .IsRequired();

        builder.Property(s => s.ChargeableEmployees)
            .HasColumnName("chargeable_employees")
            .IsRequired();

        builder.Property(s => s.PricePerEmployee)
            .HasColumnName("price_per_employee")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(s => s.Discounts)
            .HasColumnName("discounts")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(s => s.MonthlyTotal)
            .HasColumnName("monthly_total")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.HasIndex(s => s.CompanyId)
            .HasDatabaseName("ix_customer_billing_snapshots_company_id");

        builder.HasIndex(s => new { s.CompanyId, s.ComputedAt })
            .HasDatabaseName("ix_customer_billing_snapshots_company_id_computed_at");
    }
}
