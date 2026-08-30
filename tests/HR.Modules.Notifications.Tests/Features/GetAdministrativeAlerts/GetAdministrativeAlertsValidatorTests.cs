using HR.Modules.Notifications.Features.GetAdministrativeAlerts;

namespace HR.Modules.Notifications.Tests.Features.GetAdministrativeAlerts;

public class GetAdministrativeAlertsValidatorTests
{
    private static readonly GetAdministrativeAlertsValidator Validator = new();

    private static GetAdministrativeAlertsRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        PageNumber = 1,
        PageSize = 50,
    };

    [Fact]
    public void Passes_For_A_Valid_Request()
    {
        Assert.True(Validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = Validator.Validate(new GetAdministrativeAlertsRequest { CompanyId = Guid.Empty });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAdministrativeAlertsRequest.CompanyId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Fails_For_Out_Of_Range_PageSize(int pageSize)
    {
        var req = new GetAdministrativeAlertsRequest { CompanyId = Guid.NewGuid(), PageNumber = 1, PageSize = pageSize };
        Assert.False(Validator.Validate(req).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Passes_At_PageSize_Boundaries(int pageSize)
    {
        var req = new GetAdministrativeAlertsRequest { CompanyId = Guid.NewGuid(), PageNumber = 1, PageSize = pageSize };
        Assert.True(Validator.Validate(req).IsValid);
    }

    [Fact]
    public void Fails_When_PageNumber_Below_One()
    {
        var req = new GetAdministrativeAlertsRequest { CompanyId = Guid.NewGuid(), PageNumber = 0, PageSize = 50 };
        Assert.False(Validator.Validate(req).IsValid);
    }

    [Fact]
    public void Fails_When_OccurredTo_Is_Before_OccurredFrom()
    {
        var req = new GetAdministrativeAlertsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            OccurredFrom = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero),
            OccurredTo = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.False(Validator.Validate(req).IsValid);
    }

    [Fact]
    public void Passes_When_OccurredTo_Equals_OccurredFrom()
    {
        var instant = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        var req = new GetAdministrativeAlertsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            OccurredFrom = instant,
            OccurredTo = instant,
        };
        Assert.True(Validator.Validate(req).IsValid);
    }
}
