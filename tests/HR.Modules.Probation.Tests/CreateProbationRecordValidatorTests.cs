using HR.Modules.Probation.Features.CreateProbationRecord;

namespace HR.Modules.Probation.Tests;

public class CreateProbationRecordValidatorTests
{
    private readonly CreateProbationRecordValidator _validator = new();

    [Fact]
    public async Task ValidRequest_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task EmptyCompanyId_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.CompanyId));
    }

    [Fact]
    public async Task EmptyManagerEmployeeId_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.Empty,
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.ManagerEmployeeId));
    }

    [Fact]
    public async Task EmptyEmployeeId_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty,
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.EmployeeId));
    }

    [Fact]
    public async Task DefaultStartDate_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = default,
            ExpectedEndDate = new DateOnly(2026, 9, 25)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.StartDate));
    }

    [Fact]
    public async Task DefaultExpectedEndDate_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = default
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.ExpectedEndDate));
    }

    [Fact]
    public async Task Notes_ExceedingMaxLength_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25),
            Notes = new string('x', 2001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationRecordRequest.Notes));
    }

    [Fact]
    public async Task Notes_AtMaxLength_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new CreateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 6, 25),
            ExpectedEndDate = new DateOnly(2026, 9, 25),
            Notes = new string('x', 2000)
        });

        Assert.True(result.IsValid);
    }
}
