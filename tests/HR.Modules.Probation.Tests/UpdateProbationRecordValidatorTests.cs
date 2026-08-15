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

    [Fact]
    public async Task Failed_Without_DecisionFields_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Failed"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.DecisionMakerEmployeeId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.DecisionDate));
    }

    [Fact]
    public async Task Active_Without_DecisionFields_Passes()
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
    public async Task Status_Is_Case_Insensitive()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "active"
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
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active"
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
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active"
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
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active"
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
            ExpectedEndDate = default,
            Status = "Active"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExpectedEndDate));
    }

    [Fact]
    public async Task ExtensionReason_AtMaxLength_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 12, 1),
            Status = "Extended",
            ExtensionReason = new string('x', 1000)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExtensionReason_ExceedingMaxLength_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 12, 1),
            Status = "Extended",
            ExtensionReason = new string('x', 1001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExtensionReason));
    }

    [Fact]
    public async Task OutcomeNotes_ExceedingMaxLength_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active",
            OutcomeNotes = new string('x', 2001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.OutcomeNotes));
    }
}
