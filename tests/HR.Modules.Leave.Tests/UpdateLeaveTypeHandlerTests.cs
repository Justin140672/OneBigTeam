using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeaveType;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class UpdateLeaveTypeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_LeaveType()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id,
            Name = "Annual Holiday",
            Code = "ANNUAL",
            DefaultEntitlementDays = 28,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Holiday", result.Value!.Name);
        Assert.Equal(28, result.Value.DefaultEntitlementDays);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.Equal("Annual Holiday", saved.Name);
        Assert.Equal(28, saved.DefaultEntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = "Annual Leave",
            Code = "ANNUAL",
            DefaultEntitlementDays = 25,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), Guid.NewGuid(), "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = entity.Id,
            Name = "Changed",
            Code = "ANNUAL",
            DefaultEntitlementDays = 25,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Renaming_System_LeaveType()
    {
        // Item 50: a system leave type (e.g. the platform-provisioned Annual Leave) can never be
        // renamed. Other fields (code, default entitlement, accrual method, behaviour,
        // tracks-balance) remain editable.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now, isSystem: true);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id,
            Name = "Annual Holiday",
            Code = "ANNUAL",
            DefaultEntitlementDays = 28,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("system leave type", result.Error.Message);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.Equal("Annual Leave", saved.Name);
        // Non-name fields remain editable even though the rename was rejected — the whole request
        // fails atomically, so DefaultEntitlementDays should also be unchanged here.
        Assert.Equal(25, saved.DefaultEntitlementDays);
    }

    [Fact]
    public async Task HandleAsync_Updates_NonName_Fields_On_System_LeaveType_When_Name_Unchanged()
    {
        // A system leave type's other fields (default entitlement, etc.) remain editable as long
        // as the Name itself is submitted unchanged.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now, isSystem: true);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id,
            Name = "Annual Leave",
            Code = "ANNUAL",
            DefaultEntitlementDays = 30,
            AccrualMethod = AccrualMethod.Monthly,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.Equal("Annual Leave", saved.Name);
        Assert.Equal(30, saved.DefaultEntitlementDays);
        Assert.True(saved.IsSystem);
    }

    [Fact]
    public async Task HandleAsync_Renames_NonSystem_LeaveType_Unaffected_By_IsSystem_Restriction()
    {
        // Confirms the IsSystem guard is opt-in: an ordinary (non-system) leave type can still be
        // renamed exactly as before.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var entity = LeaveType.Create(Guid.NewGuid(), companyId, "Compassionate Leave", "COMPASSIONATE", 5,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, isSystem: false);
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = entity.Id,
            Name = "Bereavement Leave",
            Code = "COMPASSIONATE",
            DefaultEntitlementDays = 5,
            AccrualMethod = AccrualMethod.None,
            Behaviour = LeaveTypeBehaviour.Standard
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.LeaveTypes.SingleAsync();
        Assert.Equal("Bereavement Leave", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Code_Taken_By_Another_Type()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var first  = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);
        var second = LeaveType.Create(Guid.NewGuid(), companyId, "Sick Leave", "SICK", 10, AccrualMethod.None, LeaveTypeBehaviour.Sickness, now);
        db.LeaveTypes.AddRange(first, second);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveTypeHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateLeaveTypeRequest
        {
            CompanyId = companyId,
            Id = second.Id,
            Name = "Sick Leave",
            Code = "ANNUAL",
            DefaultEntitlementDays = 10,
            AccrualMethod = AccrualMethod.None,
            Behaviour = LeaveTypeBehaviour.Sickness
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
