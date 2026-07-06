using HR.Modules.Recruitment.Features.ListCandidateDocuments;

namespace HR.Modules.Recruitment.Tests;

public class ListCandidateDocumentsValidatorTests
{
    private readonly ListCandidateDocumentsValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ListCandidateDocumentsRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new ListCandidateDocumentsRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListCandidateDocumentsRequest.CandidateId));
    }
}
