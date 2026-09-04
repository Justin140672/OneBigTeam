using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetMyEqualityData;
using HR.Modules.Employees.Features.SaveMyEqualityData;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetMyEqualityDataHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Returns_Empty_Response_When_No_Record_Exists()
    {
        await using var db = BuildContext();

        var result = await new GetMyEqualityDataHandler(db)
            .HandleAsync(new GetMyEqualityDataRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasRecord);
        Assert.Null(result.Value.EthnicGroup);
        Assert.Null(result.Value.GenderIdentitySelfDescribed);
        Assert.Null(result.Value.CreatedAt);
    }

    [Fact]
    public async Task Returns_Mapped_Values_When_Record_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                new SaveMyEqualityDataRequest(companyId, employeeId,
                    GenderIdentity.Woman, null,
                    MarriedOrCivilPartnershipStatus.Yes,
                    EthnicGroup.SelfDescribed, "Cornish",
                    DisabilityStatus.Yes, "Chronic fatigue",
                    SexualOrientation.Bisexual, null,
                    ReligionOrBelief.NoReligion, null),
                CancellationToken.None);

        var result = await new GetMyEqualityDataHandler(db)
            .HandleAsync(new GetMyEqualityDataRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var v = result.Value!;
        Assert.True(v.HasRecord);
        Assert.Equal(GenderIdentity.Woman, v.GenderIdentity);
        Assert.Equal(MarriedOrCivilPartnershipStatus.Yes, v.MarriedOrCivilPartnershipStatus);
        Assert.Equal(EthnicGroup.SelfDescribed, v.EthnicGroup);
        Assert.Equal("Cornish", v.EthnicGroupSelfDescribed);
        Assert.Equal(DisabilityStatus.Yes, v.DisabilityStatus);
        Assert.Equal("Chronic fatigue", v.DisabilityImpact);
        Assert.Equal(SexualOrientation.Bisexual, v.SexualOrientation);
        Assert.Equal(ReligionOrBelief.NoReligion, v.ReligionOrBelief);
        Assert.Equal(new DateTimeOffset(Now), v.CreatedAt);
    }

    [Fact]
    public async Task Does_Not_Return_Another_Employees_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await new SaveMyEqualityDataHandler(db, new FakeClock(Now), new FakeAuditPublisher())
            .HandleAsync(
                new SaveMyEqualityDataRequest(companyId, Guid.NewGuid(),
                    GenderIdentity.Man, null, null, null, null, null, null, null, null, null, null),
                CancellationToken.None);

        var result = await new GetMyEqualityDataHandler(db)
            .HandleAsync(new GetMyEqualityDataRequest(companyId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasRecord);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options, new FakeSensitiveDataProtector());
    }
}
