using HR.Modules.Assets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Assets.Persistence.Configurations;

internal sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> builder)
    {
        builder.ToTable("asset_assignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.AssetId)
            .HasColumnName("asset_id")
            .IsRequired();

        builder.Property(a => a.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(a => a.AssignedBy)
            .HasColumnName("assigned_by")
            .IsRequired();

        builder.Property(a => a.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(a => a.ReturnedAt)
            .HasColumnName("returned_at");

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(a => a.CompanyId);
        builder.HasIndex(a => a.AssetId);
        builder.HasIndex(a => a.EmployeeId);
        builder.HasIndex(a => new { a.AssetId, a.ReturnedAt });
    }
}
