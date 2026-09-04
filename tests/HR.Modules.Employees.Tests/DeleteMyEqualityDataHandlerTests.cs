using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeleteMyEqualityData;
using HR.Modules.Employees.Features.SaveMyEqualityData;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeleteMyEqualityDataHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Deletes_Existing_Row_And_Publishes_Deleted_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                new SaveMyEqualityDataRequest(companyId, employeeId,
                    GenderIdentity.Man, null, null, null, null, null, null, null, null, null, null),
                CancellationToken.None);

        var publisher = new FakeAuditPublisher();
        var result = await new DeleteMyEqualityDataHandler(db, new FakeClock(Now), publisher)
            .HandleAsync(new DeleteMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await db.EmployeeEqualityData.CountAsync());
        var evt = Assert.Single(publisher.Published);
        Assert.IsType<EqualityDataDeletedAuditEvent>(evt);
    }

    [Fact]
    public async Task Returns_NotFound_When_No_Record_Exists()
    {
        await using var db = BuildContext();
        var publisher = new FakeAuditPublisher();

        var result = await new DeleteMyEqualityDataHandler(db, new FakeClock(Now), publisher)
            .HandleAsync(new DeleteMyEqualityDataRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Second_Delete_Is_A_NoOp_NotFound()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                new SaveMyEqualityDataRequest(companyId, employeeId,
                    GenderIdentity.Man, null, null, null, null, null, null, null, null, null, null),
                CancellationToken.None);

        var first = await new DeleteMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(new DeleteMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);
        var second = await new DeleteMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(new DeleteMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("not_found", second.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options, new FakeSensitiveDataProtector());
    }
}
