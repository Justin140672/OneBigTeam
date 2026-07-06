using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Recruitment.Persistence;

internal sealed class RecruitmentDbContextFactory : IDesignTimeDbContextFactory<RecruitmentDbContext>
{
    public RecruitmentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("RECRUITMENT_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<RecruitmentDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "recruitment"));

        return new RecruitmentDbContext(optionsBuilder.Options);
    }
}
