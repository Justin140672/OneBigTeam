using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentAcknowledgementSettingsValidatorTests
{
    private static readonly UpdateSharedCompanyDocumentAcknowledgementSettingsValidator Validator = new();

    [Fact]
    public void Validate_RequiresAcknowledgement_With_Null_AcknowledgementStatement_Fails()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = null,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement) &&
            e.ErrorMessage == "An acknowledgement statement is required when acknowledgement is required.");
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_Empty_AcknowledgementStatement_Fails()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = string.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement) &&
            e.ErrorMessage == "An acknowledgement statement is required when acknowledgement is required.");
    }

    [Fact]
    public void Validate_RequiresAcknowledgement_With_WhitespaceOnly_AcknowledgementStatement_Fails()
    {
        var result = Validator.Validate(new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
        {
            CompanyId               = Guid.NewGuid(),
            DocumentId              = Guid.NewGuid(),
            RequiresAcknowledgement = true,
            AcknowledgementStatement = "   ",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(UpdateSharedCompanyDocumentAcknowledgementSettingsRequest.AcknowledgementStatement) &&
            e.ErrorMessage == "An acknowledgement statement is required when acknowledgement is required.");
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
