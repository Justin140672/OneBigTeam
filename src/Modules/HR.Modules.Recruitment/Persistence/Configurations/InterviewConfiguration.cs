using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> builder)
    {
        builder.ToTable("interviews");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(i => i.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(i => i.ApplicationId)
            .HasColumnName("application_id")
            .IsRequired();

        builder.Property(i => i.InterviewerEmployeeId)
            .HasColumnName("interviewer_employee_id")
            .IsRequired();

        builder.Property(i => i.ScheduledAt)
            .HasColumnName("scheduled_at")
            .IsRequired();

        builder.Property(i => i.DurationMinutes)
            .HasColumnName("duration_minutes");

        builder.Property(i => i.Location)
            .HasColumnName("location")
            .HasMaxLength(200);

        builder.Property(i => i.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<Application>()
            .WithMany()
            .HasForeignKey(i => i.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.CompanyId);
        builder.HasIndex(i => new { i.ApplicationId, i.ScheduledAt });
    }
}
