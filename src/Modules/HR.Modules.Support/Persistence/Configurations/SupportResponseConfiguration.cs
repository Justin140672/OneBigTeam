using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Support.Persistence.Configurations;

internal sealed class SupportResponseConfiguration : IEntityTypeConfiguration<SupportResponse>
{
    public void Configure(EntityTypeBuilder<SupportResponse> builder)
    {
        builder.ToTable("support_responses");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.SupportRequestId)
            .HasColumnName("support_request_id")
            .IsRequired();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.AuthorUserId)
            .HasColumnName("author_user_id")
            .IsRequired();

        builder.Property(r => r.IsStaffResponse)
            .HasColumnName("is_staff_response")
            .IsRequired();

        builder.Property(r => r.BodyHtml)
            .HasColumnName("body_html")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.SupportRequestId);
    }
}
