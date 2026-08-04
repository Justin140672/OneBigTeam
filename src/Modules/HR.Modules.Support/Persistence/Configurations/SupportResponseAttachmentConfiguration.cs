using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Support.Persistence.Configurations;

internal sealed class SupportResponseAttachmentConfiguration : IEntityTypeConfiguration<SupportResponseAttachment>
{
    public void Configure(EntityTypeBuilder<SupportResponseAttachment> builder)
    {
        builder.ToTable("support_response_attachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.SupportResponseId)
            .HasColumnName("support_response_id")
            .IsRequired();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(a => a.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.UploadedAt)
            .HasColumnName("uploaded_at")
            .IsRequired();

        builder.HasIndex(a => a.CompanyId);
        builder.HasIndex(a => a.SupportResponseId);
    }
}
