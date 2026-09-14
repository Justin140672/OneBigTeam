using HR.Modules.Notifications.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Persistence;

internal sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();
    public DbSet<AdministrativeAlert> AdministrativeAlerts => Set<AdministrativeAlert>();
    public DbSet<OperationalAlertEmailDelivery> OperationalAlertEmailDeliveries => Set<OperationalAlertEmailDelivery>();
    public DbSet<NotificationAuditReconciliationCursor> NotificationAuditReconciliationCursors =>
        Set<NotificationAuditReconciliationCursor>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
