using HR.Modules.DataImport.Features.ValidateImportSession;

namespace HR.Modules.DataImport.Tests;

public class ValidateImportSessionValidatorTests
{
    private static readonly ValidateImportSessionValidator Validator = new();

    private static ValidateImportSessionRequest ValidRequest(IReadOnlyDictionary<string, string>? columnMapping = null) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportSessionId = Guid.NewGuid(),
        ColumnMapping = columnMapping,
    };

    [Fact]
    public void Valid_Request_With_No_ColumnMapping_Passes()
    {
        var result = Validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Request_With_WellFormed_ColumnMapping_Passes()
    {
        var request = ValidRequest(new Dictionary<string, string> { ["FirstName"] = "Given Name" });

        var result = Validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ColumnMapping_Entry_With_Empty_Key_Fails()
    {
        var request = ValidRequest(new Dictionary<string, string> { [""] = "Given Name" });

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.StartsWith(nameof(ValidateImportSessionRequest.ColumnMapping)));
    }

    [Fact]
    public void ColumnMapping_Entry_With_Empty_Value_Fails()
    {
        var request = ValidRequest(new Dictionary<string, string> { ["FirstName"] = "" });

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.StartsWith(nameof(ValidateImportSessionRequest.ColumnMapping)));
    }
}
