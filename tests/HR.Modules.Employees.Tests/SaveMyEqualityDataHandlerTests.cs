using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyEqualityData;
using HR.Modules.Employees.Features.SaveMyEqualityData;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SaveMyEqualityDataHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(3);

    private static SaveMyEqualityDataRequest Request(Guid companyId, Guid employeeId) => new(
        companyId, employeeId,
        null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Creates_Row_When_None_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var publisher = new FakeAuditPublisher();
        var handler = new SaveMyEqualityDataHandler(db, new FakeClock(Now), publisher);

        var result = await handler.HandleAsync(
            Request(companyId, employeeId) with { EthnicGroup = EthnicGroup.White },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasRecord);

        var row = await db.EmployeeEqualityData.SingleAsync();
        Assert.Equal(new DateTimeOffset(Now), row.CreatedAt);
        Assert.Equal(row.CreatedAt, row.UpdatedAt);

        var evt = Assert.Single(publisher.Published);
        var updated = Assert.IsType<EqualityDataUpdatedAuditEvent>(evt);
        Assert.True(updated.Created);
        Assert.True(updated.EthnicGroupProvided);
        Assert.False(updated.GenderIdentityProvided);
    }

    [Fact]
    public async Task Second_Call_Updates_Existing_Row_In_Place()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(Request(companyId, employeeId) with { EthnicGroup = EthnicGroup.White }, CancellationToken.None);

        var publisher = new FakeAuditPublisher();
        var result = await new SaveMyEqualityDataHandler(db, new FakeClock(Later), publisher)
            .HandleAsync(Request(companyId, employeeId) with { EthnicGroup = EthnicGroup.Mixed }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(await db.EmployeeEqualityData.ToListAsync());
        Assert.Equal(new DateTimeOffset(Now), row.CreatedAt);
        Assert.Equal(new DateTimeOffset(Later), row.UpdatedAt);
        // Answers persist as the enum member *name* (decrypted back by the context converter).
        Assert.Equal(nameof(EthnicGroup.Mixed), row.EthnicGroup);

        var updated = Assert.IsType<EqualityDataUpdatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.False(updated.Created);
    }

    [Fact]
    public async Task Does_Not_Create_A_Second_Row_For_Same_Company_And_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            await new SaveMyEqualityDataHandler(db, new FakeClock(Now.AddMinutes(i)), new FakeAuditPublisher())
                .HandleAsync(Request(companyId, employeeId) with { DisabilityStatus = DisabilityStatus.No }, CancellationToken.None);
        }

        Assert.Equal(1, await db.EmployeeEqualityData.CountAsync());
    }

    [Fact]
    public async Task SelfDescribed_Enum_And_Free_Text_Round_Trip_Through_Get_Handler()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                Request(companyId, employeeId) with
                {
                    EthnicGroup = EthnicGroup.SelfDescribed,
                    EthnicGroupSelfDescribed = "Cornish"
                },
                CancellationToken.None);

        var get = await new GetMyEqualityDataHandler(db)
            .HandleAsync(new GetMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(get.IsSuccess);
        Assert.True(get.Value!.HasRecord);
        Assert.Equal(EthnicGroup.SelfDescribed, get.Value.EthnicGroup);
        Assert.Equal("Cornish", get.Value.EthnicGroupSelfDescribed);
    }

    [Fact]
    public async Task Answers_Survive_A_Fresh_Context_Backed_By_The_Same_Store()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = BuildContext(dbName))
        {
            await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
                .HandleAsync(Request(companyId, employeeId) with { EthnicGroup = EthnicGroup.White }, CancellationToken.None);
        }

        await using var db2 = BuildContext(dbName);
        var row = await db2.EmployeeEqualityData.AsNoTracking().SingleAsync();
        Assert.Equal(nameof(EthnicGroup.White), row.EthnicGroup);
    }

    [Fact]
    public async Task CaringResponsibilities_Round_Trips_Persisted_And_Returned_In_Response()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var save = await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                Request(companyId, employeeId) with { CaringResponsibilities = CaringResponsibilities.Yes },
                CancellationToken.None);

        Assert.True(save.IsSuccess);
        Assert.Equal(CaringResponsibilities.Yes, save.Value!.CaringResponsibilities);

        var row = await db.EmployeeEqualityData.AsNoTracking().SingleAsync();
        Assert.Equal(nameof(CaringResponsibilities.Yes), row.CaringResponsibilities);

        var get = await new GetMyEqualityDataHandler(db)
            .HandleAsync(new GetMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);
        Assert.Equal(CaringResponsibilities.Yes, get.Value!.CaringResponsibilities);
    }

    [Fact]
    public async Task CaringResponsibilitiesProvided_Is_True_In_Audit_Only_When_Supplied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var withoutPublisher = new FakeAuditPublisher();
        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), withoutPublisher)
            .HandleAsync(Request(companyId, employeeId) with { EthnicGroup = EthnicGroup.White }, CancellationToken.None);
        var without = Assert.IsType<EqualityDataUpdatedAuditEvent>(Assert.Single(withoutPublisher.Published));
        Assert.False(without.CaringResponsibilitiesProvided);

        var withPublisher = new FakeAuditPublisher();
        await new SaveMyEqualityDataHandler(db, new FakeClock(Later), withPublisher)
            .HandleAsync(Request(companyId, employeeId) with { CaringResponsibilities = CaringResponsibilities.No }, CancellationToken.None);
        var with = Assert.IsType<EqualityDataUpdatedAuditEvent>(Assert.Single(withPublisher.Published));
        Assert.True(with.CaringResponsibilitiesProvided);
    }

    private static EmployeesDbContext BuildContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options, new FakeSensitiveDataProtector());
    }
}
