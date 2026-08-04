using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Persistence;

internal sealed class SupportDbContext : DbContext
{
    public SupportDbContext(DbContextOptions<SupportDbContext> options)
        : base(options)
    {
    }

    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<SupportAttachment> SupportAttachments => Set<SupportAttachment>();
    public DbSet<SupportResponse> SupportResponses => Set<SupportResponse>();
    public DbSet<SupportResponseAttachment> SupportResponseAttachments => Set<SupportResponseAttachment>();
    public DbSet<SupportNotificationAttempt> SupportNotificationAttempts => Set<SupportNotificationAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("support");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
    }
}
