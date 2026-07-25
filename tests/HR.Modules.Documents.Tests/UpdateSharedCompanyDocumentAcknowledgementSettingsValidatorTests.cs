using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentAcknowledgementSettingsValidatorTests
{
    private static readonly UpdateSharedCompanyDocumentAcknowledgementSettingsValidator Validator = new();

    // Unlike Upload, a blank statement is valid here even when RequiresAcknowledgement is true —
    // EditSharedCompanyDocumentAcknowledgementDialog.razor documents the field as optional (falling
    // back to a default placeholder shown to employees), and the handler normalizes blank input to
    // null rather than rejecting it.
    [Fact]
    public void Validate_RequiresAcknowledgement_With_Null_AcknowledgementStatement_Passes()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = null,
        });

        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_Empty_AcknowledgementStatement_Passes()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = string.Empty,
        });

        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_WhitespaceOnly_AcknowledgementStatement_Passes()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = "   ",
        });

        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_TooLong_AcknowledgementStatement_Fails()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = new string('a', 1001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_NonBlank_AcknowledgementStatement_Passes()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = "I confirm I have read the updated expenses policy.",
        });

        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }

    [Fact]
    public void Validate_AcknowledgementNotRequired_With_Null_AcknowledgementStatement_Passes()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = false,
            AcknowledgementStatement = null,
        });

        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement));
    }
}
