using HR.Modules.Probation.Features.UpdateProbationRecord;

namespace HR.Modules.Probation.Tests;

public class UpdateProbationRecordValidatorTests
{
    private readonly UpdateProbationRecordValidator _validator = new();

    [Fact]
    public async Task ValidRequest_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidRequest_With_Notes_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Notes = "Some notes."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task EmptyId_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.Empty,
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.Id));
    }

    [Fact]
    public async Task EmptyCompanyId_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.Empty,
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.CompanyId));
    }

    [Fact]
    public async Task EmptyManagerEmployeeId_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.Empty,
            ExpectedEndDate = new DateOnly(2026, 9, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ManagerEmployeeId));
    }

    [Fact]
    public async Task DefaultExpectedEndDate_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = default
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExpectedEndDate));
    }

    [Fact]
    public async Task Notes_AtMaxLength_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Notes = new string('x', 2000)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Notes_ExceedingMaxLength_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Notes = new string('x', 2001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.Notes));
    }

    [Fact]
    public async Task Null_Notes_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Notes = null
        });

        Assert.True(result.IsValid);
    }
}
