using HR.Modules.Identity.Features.GetUserAuditHistory;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests;

public class GetUserAuditHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static GetUserAuditHistoryHandler BuildHandler(
        FakeAuditHistoryReader auditHistoryReader,
        FakeTargetUserCompanyGuard? guard = null) =>
        new(auditHistoryReader, new FakeEmployeeNameReader(), guard ?? new FakeTargetUserCompanyGuard());

    [Fact]
    public async Task HandleAsync_Returns_NotFound_And_Does_Not_Read_Audit_History_When_Guard_Reports_Not_A_Member()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var auditHistoryReader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "user.invited", "UserInvite", null, null, "Invited", null, null)
        ]);
        var guard = new FakeTargetUserCompanyGuard(isMember: false);
        var handler = BuildHandler(auditHistoryReader, guard);

        var result = await handler.HandleAsync(
            new GetUserAuditHistoryRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal((companyId, employeeId), guard.LastCall);
        Assert.False(auditHistoryReader.WasCalled); // guard short-circuited before any audit read
    }

    [Fact]
    public async Task HandleAsync_Returns_Filtered_History_When_Guard_Reports_Member()
    {
        var auditHistoryReader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "user.invited", "UserInvite", null, null, "Invited", null, null),
            new AuditHistoryEntry(Now, "employee.updated", "Employee", null, null, "Updated", null, null)
        ]);
        var handler = BuildHandler(auditHistoryReader, new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetUserAuditHistoryRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(auditHistoryReader.WasCalled);
        Assert.Single(result.Value.Items);
        Assert.Equal("user.invited", result.Value.Items[0].EventType);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Member_Has_No_Events()
    {
        var auditHistoryReader = new FakeAuditHistoryReader();
        var handler = BuildHandler(auditHistoryReader, new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetUserAuditHistoryRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }
}
