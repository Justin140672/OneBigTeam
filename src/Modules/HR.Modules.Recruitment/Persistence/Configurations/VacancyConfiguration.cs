using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.ToTable("vacancies");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(v => v.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        // No FK constraint: PositionProfile is owned by HR.Modules.Employees and this module has no
        // reference to its DbContext/schema. Existence + same-company validation happens in the
        // CreateVacancy/AssignVacancyPositionProfile handlers via IPositionProfileReader. NOT NULL at
        // the DB level — see the nullability note on Vacancy.PositionProfileId for the full reasoning
        // (mandatory per explicit product direction; the review/backfill admin tooling is retained as
        // legacy/dead-in-practice code, not as a reason to keep this column nullable).
        builder.Property(v => v.PositionProfileId)
            .HasColumnName("position_profile_id")
            .IsRequired();

        // Optional recruitment-specific override of the Position Profile's canonical title — renamed
        // (via column rename, preserving existing data) from the previously-required "title" column.
        // See Vacancy.AdvertTitle's remarks.
        builder.Property(v => v.AdvertTitle)
            .HasColumnName("advert_title")
            .HasMaxLength(200);

        builder.Property(v => v.AdvertDescription)
            .HasColumnName("advert_description")
            .HasMaxLength(4000);

        builder.Property(v => v.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.HiringManagerId)
            .HasColumnName("hiring_manager_id")
            .IsRequired();

        // Nullable: an external recruitment agency may not yet be assigned. FK to ExternalRecruiter —
        // both entities live in this same module/schema, so unlike PositionProfileId this can (and
        // does) have a real database constraint. See Vacancy.AssignedRecruiterId's remarks for the
        // ticket #81 scope-correction history (previously an unconstrained Employee reference).
        builder.Property(v => v.AssignedRecruiterId)
            .HasColumnName("assigned_recruiter_id");

        builder.HasOne<ExternalRecruiter>()
            .WithMany()
            .HasForeignKey(v => v.AssignedRecruiterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(v => v.OpenedAt)
            .HasColumnName("opened_at");

        builder.Property(v => v.ClosedAt)
            .HasColumnName("closed_at");

        builder.Property(v => v.ApprovedAt)
            .HasColumnName("approved_at");

        builder.Property(v => v.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(v => v.CompanyId);
        builder.HasIndex(v => v.PositionProfileId);
        builder.HasIndex(v => v.AssignedRecruiterId);
        builder.HasIndex(v => new { v.CompanyId, v.Status });
    }
}
