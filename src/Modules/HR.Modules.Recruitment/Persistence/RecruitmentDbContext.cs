using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Persistence;

internal class RecruitmentDbContext : DbContext
{
    public RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options)
        : base(options)
    {
    }

    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<CandidateDocument> CandidateDocuments => Set<CandidateDocument>();
    public DbSet<ApplicationStageHistoryEntry> ApplicationStageHistoryEntries => Set<ApplicationStageHistoryEntry>();
    public DbSet<ExternalRecruiter> ExternalRecruiters => Set<ExternalRecruiter>();
    public DbSet<RecruitmentStage> RecruitmentStages => Set<RecruitmentStage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("recruitment");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecruitmentDbContext).Assembly);
    }
}
