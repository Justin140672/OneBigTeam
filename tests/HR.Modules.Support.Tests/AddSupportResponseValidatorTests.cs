using HR.Modules.Support.Features.AddSupportResponse;

namespace HR.Modules.Support.Tests;

public class AddSupportResponseValidatorTests
{
    private readonly AddSupportResponseValidator _validator = new();

    private static AddSupportResponseRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        BodyHtml = "<p>Thanks for reaching out, we're looking into this.</p>",
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddSupportResponseRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var request = Valid();
        request = request with { Id = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddSupportResponseRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_BodyHtml_Is_Empty()
    {
        var request = Valid();
        request = request with { BodyHtml = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddSupportResponseRequest.BodyHtml));
    }

    [Fact]
    public void Validate_Fails_When_BodyHtml_Is_Whitespace_Only()
    {
        var request = Valid();
        request = request with { BodyHtml = "   " };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddSupportResponseRequest.BodyHtml));
    }

    [Fact]
    public void Validate_Fails_When_BodyHtml_Exceeds_8000_Characters()
    {
        var request = Valid();
        request = request with { BodyHtml = new string('B', 8001) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddSupportResponseRequest.BodyHtml));
    }

    [Fact]
    public void Validate_Passes_When_BodyHtml_Is_Exactly_8000_Characters()
    {
        var request = Valid();
        request = request with { BodyHtml = new string('B', 8000) };

        Assert.True(_validator.Validate(request).IsValid);
    }
}
