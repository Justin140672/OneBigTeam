using HR.Modules.Recruitment.Features.MoveApplicationStage;

namespace HR.Modules.Recruitment.Tests;

public class MoveApplicationStageValidatorTests
{
    private readonly MoveApplicationStageValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.Empty,
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationStageRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.Empty,
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationStageRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.Empty,
            NewStageId    = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationStageRequest.ApplicationId));
    }

    [Fact]
    public void Validate_Fails_When_NewStageId_Is_Empty()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationStageRequest.NewStageId));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.NewGuid(),
            Notes         = new string('A', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationStageRequest.Notes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_Passes_When_Notes_Is_Null_Or_Empty(string? notes)
    {
        var result = _validator.Validate(new MoveApplicationStageRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            NewStageId    = Guid.NewGuid(),
            Notes         = notes,
        });

        Assert.True(result.IsValid);
    }
}
