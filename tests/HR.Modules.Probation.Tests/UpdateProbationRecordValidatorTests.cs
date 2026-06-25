using HR.Modules.Probation.Features.UpdateProbationRecord;

namespace HR.Modules.Probation.Tests;

public class UpdateProbationRecordValidatorTests
{
    private readonly UpdateProbationRecordValidator _validator = new();

    [Fact]
    public async Task ValidActiveRequest_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task InvalidStatus_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "NotAStatus"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.Status));
    }

    [Fact]
    public async Task Extended_Without_ExtensionReason_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 12, 1),
            Status = "Extended"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExtensionReason));
    }

    [Fact]
    public async Task Passed_Without_DecisionFields_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Passed"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.DecisionMakerEmployeeId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.DecisionDate));
    }

    [Fact]
    public async Task Failed_With_DecisionFields_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Failed",
            DecisionMakerEmployeeId = Guid.NewGuid(),
            DecisionDate = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsValid);
    }
}
