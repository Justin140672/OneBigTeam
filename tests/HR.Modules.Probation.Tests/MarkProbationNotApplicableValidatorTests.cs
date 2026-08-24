using HR.Modules.Probation.Features.MarkProbationNotApplicable;

namespace HR.Modules.Probation.Tests;

public class MarkProbationNotApplicableValidatorTests
{
    private readonly MarkProbationNotApplicableValidator _validator = new();

    [Fact]
    public async Task Request_With_Only_CompanyId_And_EmployeeId_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Request_With_All_Optional_Fields_Supplied_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 1, 1),
            ExpectedEndDate = new DateOnly(2026, 4, 1),
            Reason = "Exempt."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task EmptyCompanyId_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.CompanyId));
    }

    [Fact]
    public async Task EmptyEmployeeId_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.EmployeeId));
    }

    [Fact]
    public async Task Reason_AtMaxLength_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Reason = new string('x', 1000)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Reason_ExceedingMaxLength_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Reason = new string('x', 1001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.Reason));
    }

    [Fact]
    public async Task Null_Reason_Passes_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Reason = null
        });

        Assert.True(result.IsValid);
    }

    // -------- Conditional "required together" rules: partial supply of Manager/Start/End --------

    [Fact]
    public async Task Only_ManagerEmployeeId_Supplied_Fails_Validation_For_StartDate_And_ExpectedEndDate()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.StartDate));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.ExpectedEndDate));
    }

    [Fact]
    public async Task Only_StartDate_Supplied_Fails_Validation_For_ManagerEmployeeId_And_ExpectedEndDate()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 1, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.ManagerEmployeeId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.ExpectedEndDate));
    }

    [Fact]
    public async Task Only_ExpectedEndDate_Supplied_Fails_Validation_For_ManagerEmployeeId_And_StartDate()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 4, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.ManagerEmployeeId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.StartDate));
    }

    [Fact]
    public async Task ManagerEmployeeId_And_StartDate_Supplied_Without_ExpectedEndDate_Fails_Validation()
    {
        var result = await _validator.ValidateAsync(new MarkProbationNotApplicableRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 1, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MarkProbationNotApplicableRequest.ExpectedEndDate));
    }
}
