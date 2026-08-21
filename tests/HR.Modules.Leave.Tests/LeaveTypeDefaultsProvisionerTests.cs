using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveTypeDefaultsProvisionerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureDefaultLeaveTypesAsync_Creates_Default_Set_When_None_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var provisioner = new LeaveTypeDefaultsProvisioner(context, new FakeClock(FixedUtcNow));

        await provisioner.EnsureDefaultLeaveTypesAsync(companyId, CancellationToken.None);

        var names = await context.LeaveTypes.Where(lt => lt.CompanyId == companyId).Select(lt => lt.Name).ToListAsync();

        // Sick Leave is deliberately excluded from the default set.
        Assert.Equal(
            new[] { "Annual Leave", "Unpaid Leave", "Compassionate Leave", "Parental Leave", "Time Off In Lieu" }.OrderBy(n => n),
            names.OrderBy(n => n));
        Assert.DoesNotContain(names, n => n == "Sick Leave");
    }

    [Fact]
    public async Task EnsureDefaultLeaveTypesAsync_Seeds_AnnualLeave_As_System()
    {
        // Item 50: production provisioning must mark Annual Leave IsSystem=true (matching the
        // dev/E2E seed set in LeaveModule.SeedLeaveAsync), not just match it by name.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var provisioner = new LeaveTypeDefaultsProvisioner(context, new FakeClock(FixedUtcNow));

        await provisioner.EnsureDefaultLeaveTypesAsync(companyId, CancellationToken.None);

        var annualLeave = await context.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId && lt.Name == "Annual Leave");
        Assert.True(annualLeave.IsSystem);

        var others = await context.LeaveTypes
            .Where(lt => lt.CompanyId == companyId && lt.Name != "Annual Leave")
            .ToListAsync();
        Assert.All(others, lt => Assert.False(lt.IsSystem));
    }

    [Fact]
    public async Task EnsureDefaultLeaveTypesAsync_Does_Nothing_When_Company_Already_Has_Leave_Types()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.LeaveTypes.Add(LeaveType.Create(
            Guid.NewGuid(), companyId, "Custom Leave", "CUSTOM", 10, AccrualMethod.None, LeaveTypeBehaviour.Standard, now));
        await context.SaveChangesAsync();

        var provisioner = new LeaveTypeDefaultsProvisioner(context, new FakeClock(FixedUtcNow));
        await provisioner.EnsureDefaultLeaveTypesAsync(companyId, CancellationToken.None);

        var leaveType = await context.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId);
        Assert.Equal("Custom Leave", leaveType.Name);
    }

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
