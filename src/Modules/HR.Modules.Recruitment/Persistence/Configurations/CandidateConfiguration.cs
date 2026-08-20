using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("candidates");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(c => c.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(c => c.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.Phone)
            .HasColumnName("phone")
            .HasMaxLength(30);

        builder.Property(c => c.ResumeUrl)
            .HasColumnName("resume_url")
            .HasMaxLength(500);

        builder.Property(c => c.EmployeeId)
            .HasColumnName("employee_id");

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.Property(c => c.DeactivatedByUserId)
            .HasColumnName("deactivated_by_user_id");

        builder.Property(c => c.DeactivationReason)
            .HasColumnName("deactivation_reason")
            .HasMaxLength(1000);

        builder.Property(c => c.ReactivatedAt)
            .HasColumnName("reactivated_at");

        builder.Property(c => c.ReactivatedByUserId)
            .HasColumnName("reactivated_by_user_id");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(c => c.CompanyId);
        builder.HasIndex(c => new { c.CompanyId, c.Email });
        builder.HasIndex(c => new { c.CompanyId, c.IsActive });
    }
}
