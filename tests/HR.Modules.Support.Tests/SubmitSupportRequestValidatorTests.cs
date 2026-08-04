using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.SubmitSupportRequest;

namespace HR.Modules.Support.Tests;

public class SubmitSupportRequestValidatorTests
{
    private readonly SubmitSupportRequestValidator _validator = new();

    private static SubmitSupportRequestRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Type = SupportRequestType.ReportProblem,
        Title = "Leave balance not updating",
        Description = "When I approve a leave request the balance doesn't refresh.",
        Priority = SupportRequestPriority.Medium,
        IncludeDiagnostics = true,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var request = Valid();
        request = request with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Type_Is_Not_A_Defined_Enum_Value()
    {
        var request = Valid();
        request = request with { Type = (SupportRequestType)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Type));
    }

    [Fact]
    public void Validate_Fails_When_Priority_Is_Not_A_Defined_Enum_Value()
    {
        var request = Valid();
        request = request with { Priority = (SupportRequestPriority)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Priority));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var request = Valid();
        request = request with { Title = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_200_Characters()
    {
        var request = Valid();
        request = request with { Title = new string('A', 201) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Title));
    }

    [Fact]
    public void Validate_Passes_When_Title_Is_Exactly_200_Characters()
    {
        var request = Valid();
        request = request with { Title = new string('A', 200) };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Description_Is_Empty()
    {
        var request = Valid();
        request = request with { Description = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Description));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_4000_Characters()
    {
        var request = Valid();
        request = request with { Description = new string('D', 4001) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitSupportRequestRequest.Description));
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Exactly_4000_Characters()
    {
        var request = Valid();
        request = request with { Description = new string('D', 4000) };

        Assert.True(_validator.Validate(request).IsValid);
    }
}
