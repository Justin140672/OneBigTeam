using HR.Modules.Notifications.Features.ResolveAdministrativeAlert;

namespace HR.Modules.Notifications.Tests.Features.ResolveAdministrativeAlert;

public class ResolveAdministrativeAlertValidatorTests
{
    private static readonly ResolveAdministrativeAlertValidator Validator = new();

    private static ResolveAdministrativeAlertRequest Request(string? note) => new()
    {
        CompanyId = Guid.NewGuid(),
        AlertId = Guid.NewGuid(),
        ResolutionNote = note,
    };

    [Fact]
    public void Passes_When_Note_Is_Null()
    {
        Assert.True(Validator.Validate(Request(null)).IsValid);
    }

    [Fact]
    public void Passes_At_1000_Characters()
    {
        Assert.True(Validator.Validate(Request(new string('x', 1000))).IsValid);
    }

    [Fact]
    public void Fails_At_1001_Characters()
    {
        Assert.False(Validator.Validate(Request(new string('x', 1001))).IsValid);
    }

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var req = new ResolveAdministrativeAlertRequest { CompanyId = Guid.Empty, AlertId = Guid.NewGuid() };
        Assert.False(Validator.Validate(req).IsValid);
    }
}
