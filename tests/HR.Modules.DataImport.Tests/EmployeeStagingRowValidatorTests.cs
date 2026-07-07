using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;

namespace HR.Modules.DataImport.Tests;

public class EmployeeStagingRowValidatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static EmployeeStagingRowValidator BuildValidator(FakeEmployeeImportLookupReader? reader = null) =>
        new(reader ?? new FakeEmployeeImportLookupReader());

    // Builds a row that otherwise satisfies all required fields, with optional extras set.
    private static ParsedImportRow ValidRow(
        int rowNumber,
        string firstName = "Alice",
        string lastName = "Smith",
        string? workEmail = "alice@example.com",
        string? startDate = "2026-01-01",
        string? employeeNumber = null,
        string? managerReference = null,
        string? dateOfBirth = null,
        string? continuousServiceDate = null,
        string? probationEndDate = null,
        string? salaryAmount = null,
        string? salaryType = null,
        string? currency = null,
        string? hoursPerWeek = null,
        string? fte = null,
        string? leaveTypeCode = null,
        string? leaveBalanceDays = null)
    {
        var fields = new Dictionary<string, string?>
        {
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["WorkEmail"] = workEmail,
            ["StartDate"] = startDate,
        };

        if (employeeNumber is not null) fields["EmployeeNumber"] = employeeNumber;
        if (managerReference is not null) fields["ManagerReference"] = managerReference;
        if (dateOfBirth is not null) fields["DateOfBirth"] = dateOfBirth;
        if (continuousServiceDate is not null) fields["ContinuousServiceDate"] = continuousServiceDate;
        if (probationEndDate is not null) fields["ProbationEndDate"] = probationEndDate;
        if (salaryAmount is not null) fields["SalaryAmount"] = salaryAmount;
        if (salaryType is not null) fields["SalaryType"] = salaryType;
        if (currency is not null) fields["Currency"] = currency;
        if (hoursPerWeek is not null) fields["HoursPerWeek"] = hoursPerWeek;
        if (fte is not null) fields["FTE"] = fte;
        if (leaveTypeCode is not null) fields["LeaveTypeCode"] = leaveTypeCode;
        if (leaveBalanceDays is not null) fields["LeaveBalanceDays"] = leaveBalanceDays;

        return new ParsedImportRow(rowNumber, fields);
    }

    // Derives the "mapped fields" set from whichever fields are present across the given rows,
    // mirroring how the parser reports a field as mapped only when its header was found in the file.
    private static IReadOnlySet<string> MappedFieldsFrom(params ParsedImportRow[] rows) =>
        rows.SelectMany(r => r.Fields.Keys).ToHashSet();

    [Fact]
    public async Task ValidateAsync_HappyPath_Produces_No_Errors()
    {
        var validator = BuildValidator();
        var row = ValidRow(2);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_FirstName()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, firstName: null!);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'FirstName' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_LastName()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, lastName: null!);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'LastName' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_WorkEmail()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, workEmail: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'WorkEmail' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_StartDate()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, startDate: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'StartDate' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Duplicate_EmployeeNumber_Within_File_On_Both_Rows()
    {
        var validator = BuildValidator();
        var row1 = ValidRow(2, workEmail: "row1@example.com", employeeNumber: "EMP1");
        var row2 = ValidRow(3, workEmail: "row2@example.com", employeeNumber: " emp1 "); // case-insensitive/trimmed match

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.False(r.IsValid));
        Assert.All(results, r => Assert.Contains(r.Errors, e => e.Contains("Duplicate employee number")));
    }

    [Fact]
    public async Task ValidateAsync_Flags_EmployeeNumber_That_Already_Exists_In_Company()
    {
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedExistingEmployeeNumber("EMP99");
        var validator = BuildValidator(reader);
        var row = ValidRow(2, employeeNumber: "EMP99");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists in this company"));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Duplicate_WorkEmail_Within_File_On_Both_Rows()
    {
        var validator = BuildValidator();
        var row1 = ValidRow(2, workEmail: "Dup@Example.com");
        var row2 = ValidRow(3, workEmail: " dup@example.com "); // case-insensitive/trimmed match

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.False(r.IsValid));
        Assert.All(results, r => Assert.Contains(r.Errors, e => e.Contains("Duplicate work email")));
    }

    [Fact]
    public async Task ValidateAsync_Flags_WorkEmail_That_Already_Exists_In_Company()
    {
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedExistingWorkEmail("existing@example.com");
        var validator = BuildValidator(reader);
        var row = ValidRow(2, workEmail: "existing@example.com");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists in this company"));
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("not-a-date")]
    public async Task ValidateAsync_Flags_Invalid_StartDate_Format(string invalidDate)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, startDate: invalidDate);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("is not a valid date"));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Both_Supported_Date_Formats()
    {
        var validator = BuildValidator();
        var row1 = ValidRow(2, workEmail: "a@example.com", startDate: "2026-01-15");
        var row2 = ValidRow(3, workEmail: "b@example.com", startDate: "15/01/2026");

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public async Task ValidateAsync_Flags_DateOfBirth_That_Is_Today()
    {
        var validator = BuildValidator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var row = ValidRow(2, dateOfBirth: today);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'DateOfBirth' must be in the past."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_DateOfBirth_In_The_Future()
    {
        var validator = BuildValidator();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        var row = ValidRow(2, dateOfBirth: tomorrow);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'DateOfBirth' must be in the past."));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_DateOfBirth_In_The_Past()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, dateOfBirth: "1990-05-01");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Accepts_ManagerReference_Matching_Another_Row_In_File_By_EmployeeNumber()
    {
        var validator = BuildValidator();
        var manager = ValidRow(2, workEmail: "manager@example.com", employeeNumber: "MGR1");
        var report = ValidRow(3, workEmail: "report@example.com", managerReference: "MGR1");

        var results = await validator.ValidateAsync(CompanyId, [manager, report], MappedFieldsFrom(manager, report), CancellationToken.None);

        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_ManagerReference_Matching_Another_Row_In_File_By_WorkEmail()
    {
        var validator = BuildValidator();
        var manager = ValidRow(2, workEmail: "manager@example.com");
        var report = ValidRow(3, workEmail: "report@example.com", managerReference: "manager@example.com");

        var results = await validator.ValidateAsync(CompanyId, [manager, report], MappedFieldsFrom(manager, report), CancellationToken.None);

        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_ManagerReference_Resolving_To_Existing_Employee()
    {
        var existingManagerId = Guid.NewGuid();
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedReference("existing.manager@example.com", existingManagerId);
        var validator = BuildValidator(reader);

        var row = ValidRow(2, managerReference: "existing.manager@example.com");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Unresolvable_ManagerReference()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, managerReference: "nobody@example.com");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not match any employee"));
    }

    [Fact]
    public async Task ValidateAsync_Skips_Compensation_Validation_When_No_Compensation_Column_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "not-a-number");

        // SalaryAmount is present on the row but not reported as a "mapped" field
        // (simulating a column the parser found nowhere in the file's header row).
        var mappedFields = new HashSet<string> { "FirstName", "LastName", "WorkEmail", "StartDate" };

        var results = await validator.ValidateAsync(CompanyId, [row], mappedFields, CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Validates_SalaryAmount_When_Compensation_Column_Is_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "not-a-number");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryAmount'") && e.Contains("positive number"));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Negative_SalaryAmount_When_Compensation_Column_Is_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "-100");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryAmount'"));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Invalid_SalaryType()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryType: "Monthly"); // not one of Annual/Hourly/Daily

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryType'"));
    }

    [Theory]
    [InlineData("Annual")]
    [InlineData("hourly")]
    [InlineData("DAILY")]
    public async Task ValidateAsync_Accepts_Valid_SalaryType_Case_Insensitively(string salaryType)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryType: salaryType);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Invalid_Currency_Format()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, currency: "US");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Currency'"));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Non_Positive_HoursPerWeek()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, hoursPerWeek: "0");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'HoursPerWeek'"));
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public async Task ValidateAsync_Flags_FTE_Out_Of_Bounds(string fte)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, fte: fte);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'FTE'"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.5")]
    public async Task ValidateAsync_Accepts_FTE_Within_Bounds(string fte)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, fte: fte);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Skips_Leave_Validation_When_No_Leave_Column_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, leaveBalanceDays: "-5");

        var mappedFields = new HashSet<string> { "FirstName", "LastName", "WorkEmail", "StartDate" };

        var results = await validator.ValidateAsync(CompanyId, [row], mappedFields, CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Negative_LeaveBalanceDays_When_Leave_Column_Is_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, leaveBalanceDays: "-5");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'LeaveBalanceDays'"));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Zero_LeaveBalanceDays_When_Leave_Column_Is_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, leaveBalanceDays: "0");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }
}
