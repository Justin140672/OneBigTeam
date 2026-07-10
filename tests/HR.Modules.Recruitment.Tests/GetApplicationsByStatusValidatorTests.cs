using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetApplicationsByStatus;

namespace HR.Modules.Recruitment.Tests;

public class GetApplicationsByStatusValidatorTests
{
    private readonly GetApplicationsByStatusValidator _validator = new();

    [Theory]
    [InlineData((int)ApplicationStatus.Applied)]
    [InlineData((int)ApplicationStatus.Screening)]
    [InlineData((int)ApplicationStatus.InterviewScheduled)]
    [InlineData((int)ApplicationStatus.Interviewed)]
    [InlineData((int)ApplicationStatus.Offered)]
    [InlineData((int)ApplicationStatus.Hired)]
    public void Validate_Passes_For_Each_Active_Pipeline_Stage(int statusValue)
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.NewGuid(), (ApplicationStatus)statusValue));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData((int)ApplicationStatus.Rejected)]
    [InlineData((int)ApplicationStatus.Withdrawn)]
    public void Validate_Fails_For_Rejected_Or_Withdrawn_Status(int statusValue)
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.NewGuid(), (ApplicationStatus)statusValue));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetApplicationsByStatusRequest.Status));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.Empty, ApplicationStatus.Applied));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetApplicationsByStatusRequest.CompanyId));
    }
}
