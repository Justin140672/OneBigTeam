using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;

namespace HR.Modules.DataImport.Tests;

public class EmployeeStagingRowValidatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static EmployeeStagingRowValidator BuildValidator(
        FakeEmployeeImportLookupReader? reader = null,
        FakeImportLookupResolver? resolver = null) =>
        new(reader ?? new FakeEmployeeImportLookupReader(), resolver ?? new FakeImportLookupResolver());

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
        string? leaveBalanceDays = null,
        string? departmentName = null,
        string? employmentTypeName = null,
        string? locationName = null,
        string? positionProfileTitle = null)
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
        if (departmentName is not null) fields["DepartmentName"] = departmentName;
        if (employmentTypeName is not null) fields["EmploymentTypeName"] = employmentTypeName;
        if (locationName is not null) fields["LocationName"] = locationName;
        if (positionProfileTitle is not null) fields["PositionProfileTitle"] = positionProfileTitle;

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

    [Fact]
    public async Task ValidateAsync_Auto_Creates_Unseeded_Department_EmploymentType_And_Location_With_Warnings()
    {
        var validator = BuildValidator();
        var row = ValidRow(
            2,
            departmentName: "Sales",
            employmentTypeName: "Contractor",
            locationName: "London");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsValid);
        Assert.NotNull(result.DepartmentId);
        Assert.NotNull(result.LocationId);
        Assert.NotNull(result.EmploymentTypeId);
        Assert.Contains(result.Warnings, w => w.Contains("Department 'Sales' did not exist and was created."));
        Assert.Contains(result.Warnings, w => w.Contains("Employment Type 'Contractor' did not exist and was created."));
        Assert.Contains(result.Warnings, w => w.Contains("Location 'London' did not exist and was created."));
    }

    [Fact]
    public async Task ValidateAsync_Resolves_Seeded_Department_EmploymentType_And_Location_Without_Warnings()
    {
        var existingDepartmentId = Guid.NewGuid();
        var existingEmploymentTypeId = Guid.NewGuid();
        var existingLocationId = Guid.NewGuid();

        var resolver = new FakeImportLookupResolver();
        resolver.SeedExistingDepartment(CompanyId, "Sales", existingDepartmentId);
        resolver.SeedExistingEmploymentType(CompanyId, "Contractor", existingEmploymentTypeId);
        resolver.SeedExistingLocation(CompanyId, "London", existingLocationId);

        var validator = BuildValidator(resolver: resolver);
        var row = ValidRow(
            2,
            departmentName: "Sales",
            employmentTypeName: "Contractor",
            locationName: "London");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsValid);
        Assert.Equal(existingDepartmentId, result.DepartmentId);
        Assert.Equal(existingEmploymentTypeId, result.EmploymentTypeId);
        Assert.Equal(existingLocationId, result.LocationId);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("did not exist and was created."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_PositionProfile_Without_Department_Or_Location_As_Error()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, positionProfileTitle: "Software Developer");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Null(result.PositionProfileId);
        Assert.Contains(
            result.Errors,
            e => e.Contains("could not be created because both Department and Location must be present and resolvable"));
    }

    [Fact]
    public async Task ValidateAsync_Auto_Creates_PositionProfile_When_Department_And_Location_Resolvable()
    {
        var validator = BuildValidator();
        var row = ValidRow(
            2,
            departmentName: "Sales",
            locationName: "London",
            positionProfileTitle: "Software Developer");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsValid);
        Assert.NotNull(result.PositionProfileId);
        Assert.Contains(result.Warnings, w => w.Contains("Position Profile 'Software Developer' did not exist and was created."));
    }

    [Fact]
    public async Task ValidateAsync_Resolves_Seeded_PositionProfile_Without_Warning_Regardless_Of_Department_Or_Location()
    {
        var existingProfileId = Guid.NewGuid();
        var resolver = new FakeImportLookupResolver();
        resolver.SeedExistingPositionProfile(CompanyId, "Software Developer", existingProfileId);

        var validator = BuildValidator(resolver: resolver);
        var row = ValidRow(2, positionProfileTitle: "Software Developer");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsValid);
        Assert.Equal(existingProfileId, result.PositionProfileId);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Position Profile"));
    }

    // ValidateWorkingPatternFields is only invoked when WorkingDays or HoursPerDay is a mapped
    // column, mirroring the compensation/leave gating tested above.
    private static ParsedImportRow ValidRowWithWorkingPattern(
        int rowNumber, string? workingDays = null, string? hoursPerDay = null)
    {
        var row = ValidRow(rowNumber);
        var fields = new Dictionary<string, string?>(row.Fields);
        if (workingDays is not null) fields["WorkingDays"] = workingDays;
        if (hoursPerDay is not null) fields["HoursPerDay"] = hoursPerDay;
        return new ParsedImportRow(rowNumber, fields);
    }

    [Fact]
    public async Task ValidateAsync_Skips_WorkingPattern_Validation_When_No_WorkingPattern_Column_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, workingDays: "NotADay");

        var mappedFields = new HashSet<string> { "FirstName", "LastName", "WorkEmail", "StartDate" };

        var results = await validator.ValidateAsync(CompanyId, [row], mappedFields, CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Valid_Comma_Separated_WorkingDays_List()
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, workingDays: "Monday,Tuesday,Wednesday,Thursday,Friday");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Accepts_WorkingDays_Case_Insensitively()
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, workingDays: "monday,TUESDAY,Wednesday");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Invalid_Day_Name_In_WorkingDays()
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, workingDays: "Monday,Funday");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'WorkingDays'") && e.Contains("Funday"));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Positive_HoursPerDay()
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, hoursPerDay: "7.5");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("not-a-number")]
    public async Task ValidateAsync_Flags_Non_Positive_Or_Invalid_HoursPerDay(string hoursPerDay)
    {
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, hoursPerDay: hoursPerDay);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'HoursPerDay'"));
    }
}
