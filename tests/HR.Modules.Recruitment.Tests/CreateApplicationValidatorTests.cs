using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateApplication;

namespace HR.Modules.Recruitment.Tests;

public class CreateApplicationValidatorTests
{
    private readonly CreateApplicationValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.Empty,
            CandidateId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.CandidateId));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Notes       = new string('A', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.Notes));
    }

    [Fact]
    public void Validate_Fails_When_Source_ExternalRecruiter_But_SourceExternalRecruiterId_Missing()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Source      = ApplicationSource.ExternalRecruiter,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.SourceExternalRecruiterId));
    }

    [Fact]
    public void Validate_Passes_When_Source_ExternalRecruiter_And_SourceExternalRecruiterId_Supplied()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId                 = Guid.NewGuid(),
            VacancyId                 = Guid.NewGuid(),
            CandidateId               = Guid.NewGuid(),
            Source                    = ApplicationSource.ExternalRecruiter,
            SourceExternalRecruiterId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_SourceExternalRecruiterId_Supplied_But_Source_Is_Not_ExternalRecruiter()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId                 = Guid.NewGuid(),
            VacancyId                 = Guid.NewGuid(),
            CandidateId               = Guid.NewGuid(),
            Source                    = ApplicationSource.Direct,
            SourceExternalRecruiterId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.SourceExternalRecruiterId));
    }

    [Fact]
    public void Validate_Passes_When_Source_And_SourceExternalRecruiterId_Both_Omitted()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }
}
