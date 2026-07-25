using HR.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Employees.Persistence.Configurations;

internal sealed class EmployeeNoteConfiguration : IEntityTypeConfiguration<EmployeeNote>
{
    public void Configure(EntityTypeBuilder<EmployeeNote> builder)
    {
        builder.ToTable("employee_notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(n => n.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(n => n.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(n => n.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.NoteText)
            .HasColumnName("note_text")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(n => n.IsImportant)
            .HasColumnName("is_important")
            .IsRequired();

        builder.Property(n => n.IsSuperseded)
            .HasColumnName("is_superseded")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(n => n.SupersededByNoteId)
            .HasColumnName("superseded_by_note_id");

        builder.HasOne<EmployeeNote>()
            .WithMany()
            .HasForeignKey(n => n.SupersededByNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(n => n.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(n => n.CreatedDate)
            .HasColumnName("created_date")
            .IsRequired();

        builder.HasIndex(n => n.CompanyId);
        builder.HasIndex(n => new { n.CompanyId, n.EmployeeId });
    }
}
