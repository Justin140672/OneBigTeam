using HR.Modules.Leave.Domain;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Persistence;

internal sealed class LeaveDbContext : DbContext
{
    public LeaveDbContext(DbContextOptions<LeaveDbContext> options)
        : base(options)
    {
    }

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<EmployeeLeavePolicyAssignment> EmployeeLeavePolicyAssignments => Set<EmployeeLeavePolicyAssignment>();
    public DbSet<ToilTransaction> ToilTransactions => Set<ToilTransaction>();
    public DbSet<LeaveBalanceAdjustment> LeaveBalanceAdjustments => Set<LeaveBalanceAdjustment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditOutboxEntry> AuditOutboxEntries => Set<AuditOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("leave");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeaveDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
        modelBuilder.ApplyConfiguration(new AuditOutboxEntryConfiguration<AuditOutboxEntry>());
    }
}
