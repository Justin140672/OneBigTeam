using HR.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Reporting.Persistence.Configurations;

internal sealed class ReportFavouriteConfiguration : IEntityTypeConfiguration<ReportFavourite>
{
    public void Configure(EntityTypeBuilder<ReportFavourite> builder)
    {
        builder.ToTable("report_favourites");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(f => f.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(f => f.ReportId)
            .HasColumnName("report_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(f => new { f.CompanyId, f.UserId, f.ReportId })
            .IsUnique();
    }
}
