using HR.Modules.Probation.Features.UpdateProbationRecord;
using HR.SharedKernel;

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
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            ExpectedVersion = 1
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
            Notes = "Some notes.",
            ExpectedVersion = 1
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
            ExpectedVersion = 1
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
            ExpectedVersion = 1
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
            ExpectedVersion = 1
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
            ExpectedVersion = 1
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
            Notes = new string('x', 2000),
            ExpectedVersion = 1
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
            Notes = new string('x', 2001),
            ExpectedVersion = 1
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
            Notes = null,
            ExpectedVersion = 1
        });

        Assert.True(result.IsValid);
    }

    // Ticket 16 (optimistic concurrency): ExpectedVersion is mandatory on this protected update.

    [Fact]
    public async Task Null_ExpectedVersion_Fails_With_MissingVersionMessage()
    {
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            ExpectedVersion = null
        });

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExpectedVersion));
        Assert.Equal(ConcurrencyValidationExtensions.MissingVersionMessage, error.ErrorMessage);
    }

    [Fact]
    public async Task Zero_ExpectedVersion_Passes_Version_Rule()
    {
        // Zero is a legal (if unusual) version value — the rule only guards against a missing
        // (null) version, not against any particular numeric value.
        var result = await _validator.ValidateAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            ExpectedVersion = 0
        });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateProbationRecordRequest.ExpectedVersion));
    }
}
