using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.StartLeavingProcess;

namespace HR.Modules.Employees.Tests;

public class StartLeavingProcessValidatorTests
{
    private static StartLeavingProcessRequest ValidRequest() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ResignationReceivedDate: new DateOnly(2026, 7, 1),
            LeavingDate: new DateOnly(2026, 8, 1),
            LastWorkingDay: new DateOnly(2026, 7, 31),
            LeavingReason.Resignation);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new StartLeavingProcessValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_ResignationReceivedDate_Is_Default()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { ResignationReceivedDate = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.ResignationReceivedDate));
    }

    [Fact]
    public void Validate_Fails_When_LeavingDate_Is_Default()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingDate = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.LeavingDate));
    }

    [Fact]
    public void Validate_Fails_When_LastWorkingDay_Is_Default()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LastWorkingDay = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.LastWorkingDay));
    }

    [Fact]
    public void Validate_Fails_When_LeavingDate_Is_Before_ResignationReceivedDate()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with
        {
            ResignationReceivedDate = new DateOnly(2026, 7, 10),
            LeavingDate = new DateOnly(2026, 7, 5),
            LastWorkingDay = new DateOnly(2026, 7, 5)
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(StartLeavingProcessRequest.LeavingDate)
            && e.ErrorMessage == "LeavingDate must be on or after ResignationReceivedDate.");
    }

    [Fact]
    public void Validate_Fails_When_LastWorkingDay_Is_After_LeavingDate()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with
        {
            LeavingDate = new DateOnly(2026, 7, 31),
            LastWorkingDay = new DateOnly(2026, 8, 1)
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(StartLeavingProcessRequest.LastWorkingDay)
            && e.ErrorMessage == "LastWorkingDay must be on or before LeavingDate.");
    }

    [Fact]
    public void Validate_Passes_When_LeavingDate_Equals_ResignationReceivedDate()
    {
        var validator = new StartLeavingProcessValidator();
        var date = new DateOnly(2026, 7, 1);
        var request = ValidRequest() with
        {
            ResignationReceivedDate = date,
            LeavingDate = date,
            LastWorkingDay = date
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeavingReason_Is_Invalid_Enum_Value()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = (LeavingReason)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.LeavingReason));
    }

    [Fact]
    public void Validate_Fails_When_ReplacementManagerEmployeeId_Equals_EmployeeId()
    {
        var validator = new StartLeavingProcessValidator();
        var employeeId = Guid.NewGuid();
        var request = ValidRequest() with { EmployeeId = employeeId, ReplacementManagerEmployeeId = employeeId };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.ReplacementManagerEmployeeId));
    }

    [Fact]
    public void Validate_Passes_When_ReplacementManagerEmployeeId_Differs_From_EmployeeId()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { ReplacementManagerEmployeeId = Guid.NewGuid() };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_ReplacementManagerEmployeeId_Is_Null()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { ReplacementManagerEmployeeId = null };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    // ---- Spec SPEC-OFF-01: Notes required when LeavingReason == Other ----

    [Fact]
    public void Validate_Fails_When_LeavingReason_Is_Other_And_Notes_Is_Null()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = LeavingReason.Other, Notes = null };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.Notes));
    }

    [Fact]
    public void Validate_Fails_When_LeavingReason_Is_Other_And_Notes_Is_Empty_String()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = LeavingReason.Other, Notes = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.Notes));
    }

    // NotEmpty() treats whitespace-only strings as invalid, distinct from a bare length check —
    // pinned explicitly rather than relying on the null/empty cases above alone.
    [Fact]
    public void Validate_Fails_When_LeavingReason_Is_Other_And_Notes_Is_Whitespace_Only()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = LeavingReason.Other, Notes = "   " };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartLeavingProcessRequest.Notes));
    }

    [Fact]
    public void Validate_Passes_When_LeavingReason_Is_Other_And_Notes_Is_Provided()
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = LeavingReason.Other, Notes = "Employee is emigrating." };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    // Negated branch of the `When` condition: every non-Other reason must NOT require Notes.
    [Theory]
    [InlineData((int)LeavingReason.Resignation)]
    [InlineData((int)LeavingReason.Redundancy)]
    [InlineData((int)LeavingReason.Dismissal)]
    [InlineData((int)LeavingReason.Retirement)]
    [InlineData((int)LeavingReason.EndOfContract)]
    [InlineData((int)LeavingReason.MutualAgreement)]
    public void Validate_Passes_When_LeavingReason_Is_Not_Other_And_Notes_Is_Null(int reasonValue)
    {
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = (LeavingReason)reasonValue, Notes = null };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    // LeavingReason is internal, so the theory parameter must be a publicly-accessible type
    // (int) to avoid CS0051 — the enum value is cast back internally in the method body.
    [Theory]
    [InlineData((int)LeavingReason.Resignation)]
    [InlineData((int)LeavingReason.Redundancy)]
    [InlineData((int)LeavingReason.Dismissal)]
    [InlineData((int)LeavingReason.Retirement)]
    [InlineData((int)LeavingReason.EndOfContract)]
    [InlineData((int)LeavingReason.MutualAgreement)]
    [InlineData((int)LeavingReason.Other)]
    public void Validate_Passes_For_Every_Valid_LeavingReason(int reasonValue)
    {
        var reason = (LeavingReason)reasonValue;
        var validator = new StartLeavingProcessValidator();
        var request = ValidRequest() with
        {
            LeavingReason = reason,
            Notes = reason == LeavingReason.Other ? "Explanatory notes." : null
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
