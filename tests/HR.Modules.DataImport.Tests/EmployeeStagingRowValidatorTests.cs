using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;

namespace HR.Modules.DataImport.Tests;

public class EmployeeStagingRowValidatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static EmployeeStagingRowValidator BuildValidator(
        FakeEmployeeImportLookupReader? reader = null,
        FakeImportLookupResolver? resolver = null,
        FakeCompanyEmployeeNumberSettingsReader? employeeNumberSettingsReader = null) =>
        new(
            reader ?? new FakeEmployeeImportLookupReader(),
            resolver ?? new FakeImportLookupResolver(),
            employeeNumberSettingsReader ?? new FakeCompanyEmployeeNumberSettingsReader());

    // Builds a row that otherwise satisfies all required fields, with optional extras set.
    // DateOfBirth/Nationality/Gender/EmployeeNumber/DepartmentName/LocationName/
    // EmploymentTypeName/PositionProfileTitle/SalaryAmount all default to valid values (rather
    // than being omitted) since they are mandatory Employee fields — tests that need to exercise
    // a specific missing/absent scenario for one of them pass that parameter as an explicit
    // `null` to override the default.
    private static ParsedImportRow ValidRow(
        int rowNumber,
        string firstName = "Alice",
        string lastName = "Smith",
        string? workEmail = "alice@example.com",
        string? startDate = "2026-01-01",
        string? employeeNumber = "EMP-0001",
        string? managerReference = null,
        string? dateOfBirth = "1990-01-01",
        string? nationality = "British",
        string? gender = "Female",
        string? continuousServiceDate = null,
        string? probationEndDate = null,
        string? salaryAmount = "50000",
        string? salaryType = null,
        string? currency = null,
        string? hoursPerWeek = null,
        string? fte = null,
        string? leaveTypeCode = null,
        string? leaveBalanceDays = null,
        string? departmentName = "Engineering",
        string? employmentTypeName = "Permanent",
        string? locationName = "London",
        string? positionProfileTitle = "Developer")
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
        if (nationality is not null) fields["Nationality"] = nationality;
        if (gender is not null) fields["Gender"] = gender;
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
    public async Task ValidateAsync_Flags_Missing_Nationality()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, nationality: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Nationality' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_Gender()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, gender: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Gender' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_EmploymentTypeName()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, employmentTypeName: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'EmploymentTypeName' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_DepartmentName()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, departmentName: null, locationName: null, positionProfileTitle: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'DepartmentName' is required."));
    }

    [Fact]
    public async Task ValidateAsync_Flags_Missing_PositionProfileTitle()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, positionProfileTitle: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'PositionProfileTitle' is required."));
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
        var row1 = ValidRow(2, workEmail: "a@example.com", startDate: "2026-01-15", employeeNumber: "EMP-0001");
        var row2 = ValidRow(3, workEmail: "b@example.com", startDate: "15/01/2026", employeeNumber: "EMP-0002");

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
        var manager = ValidRow(2, workEmail: "manager@example.com", employeeNumber: "EMP-0001");
        var report = ValidRow(3, workEmail: "report@example.com", managerReference: "manager@example.com", employeeNumber: "EMP-0002");

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
    public async Task ValidateAsync_Flags_ManagerReference_Pointing_At_Its_Own_Row()
    {
        // A row referencing its own EmployeeNumber/WorkEmail must not "self-approve" via the
        // in-file match check (which explicitly excludes r.RowNumber != row.RowNumber), and must
        // also fail the existing-employee lookup since no such employee exists yet.
        var validator = BuildValidator();
        var row = ValidRow(2, workEmail: "self@example.com", employeeNumber: "SELF1", managerReference: "SELF1");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not match any employee"));
    }

    [Fact]
    public async Task ValidateAsync_Skips_Compensation_Validation_When_No_Compensation_Column_Mapped()
    {
        var validator = BuildValidator();
        // SalaryAmount is unconditionally required/validated regardless of mapped columns (see
        // ValidateAsync_Validates_SalaryAmount_Format_Even_When_No_Compensation_Column_Mapped
        // below), so it's given a valid value here — this test is only about the *other*
        // compensation fields (SalaryType/Currency/HoursPerWeek/FTE) still being skipped when
        // none of them are reported as mapped.
        var row = ValidRow(2, salaryAmount: "50000", currency: "not-a-currency");

        // Currency is present on the row but not reported as a "mapped" field (simulating a
        // column the parser found nowhere in the file's header row).
        var mappedFields = new HashSet<string> { "FirstName", "LastName", "WorkEmail", "StartDate", "SalaryAmount" };

        var results = await validator.ValidateAsync(CompanyId, [row], mappedFields, CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Validates_SalaryAmount_Format_Even_When_No_Compensation_Column_Mapped()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "not-a-number");

        // Unlike SalaryType/Currency/HoursPerWeek/FTE, SalaryAmount's format is checked
        // unconditionally (ValidateSalaryAmountFormat), independent of which columns are mapped.
        var mappedFields = new HashSet<string> { "FirstName", "LastName", "WorkEmail", "StartDate" };

        var results = await validator.ValidateAsync(CompanyId, [row], mappedFields, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryAmount'") && e.Contains("positive number"));
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
    public async Task ValidateAsync_Flags_Missing_SalaryAmount_When_SalaryType_Is_Mapped()
    {
        var validator = BuildValidator();
        // SalaryAmount itself is blank/omitted, but SalaryType is mapped for the import — any
        // mapped compensation column makes SalaryAmount mandatory.
        var row = ValidRow(2, salaryAmount: null, salaryType: "Annual");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryAmount' is required."));
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
        var row = ValidRow(2, salaryAmount: "50000", salaryType: salaryType);

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

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task ValidateAsync_Flags_Non_Positive_HoursPerWeek(string hoursPerWeek)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, hoursPerWeek: hoursPerWeek);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'HoursPerWeek'"));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Positive_HoursPerWeek()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "50000", hoursPerWeek: "37.5");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Zero_SalaryAmount()
    {
        // The check is `salary <= 0`, not `< 0` — zero must be rejected too, not just negatives.
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "0");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'SalaryAmount'") && e.Contains("positive number"));
    }

    [Fact]
    public async Task ValidateAsync_Accepts_Valid_Currency_Case_Insensitively()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "50000", currency: "usd");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Flags_Currency_With_Too_Many_Letters()
    {
        var validator = BuildValidator();
        var row = ValidRow(2, currency: "USDD");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Currency'"));
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public async Task ValidateAsync_Flags_FTE_Out_Of_Bounds(string fte)
    {
        var validator = BuildValidator();
        var row = ValidRow(2, salaryAmount: "50000", fte: fte);

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
        var row = ValidRow(2, salaryAmount: "50000", fte: fte);

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
        resolver.SeedExistingPositionProfile(CompanyId, "Developer", Guid.NewGuid());

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
        var row = ValidRow(2, departmentName: null, locationName: null, positionProfileTitle: "Software Developer");

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
    public async Task ValidateAsync_Flags_WorkingDays_That_Is_Only_Separators()
    {
        // After TrimEntries + RemoveEmptyEntries splitting, a value of only commas/whitespace
        // yields zero day names — must hit the "at least one day name" branch, not silently pass.
        var validator = BuildValidator();
        var row = ValidRowWithWorkingPattern(2, workingDays: " , , ");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'WorkingDays' must contain at least one day name."));
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

    // --- EmployeeNumberMode-aware validation (Manual vs Automatic) ---

    [Fact]
    public async Task ValidateAsync_Manual_Mode_Flags_Missing_EmployeeNumber()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row = ValidRow(2, employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'EmployeeNumber' is required."));
    }

    [Theory]
    [InlineData("EMP#001")]
    [InlineData("EMP@001")]
    public async Task ValidateAsync_Manual_Mode_Flags_Invalid_EmployeeNumber_Format(string employeeNumber)
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row = ValidRow(2, employeeNumber: employeeNumber);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.Contains("Employee number may only contain letters, numbers, spaces, and the separators - _ . / (max 50 characters)."));
    }

    [Fact]
    public async Task ValidateAsync_Manual_Mode_Flags_EmployeeNumber_Exceeding_Max_Length()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row = ValidRow(2, employeeNumber: new string('A', 51));

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.Contains("Employee number may only contain letters, numbers, spaces, and the separators - _ . / (max 50 characters)."));
    }

    [Theory]
    [InlineData("EMP-0001")]
    [InlineData("EMP_0001")]
    [InlineData("EMP 0001")]
    [InlineData("EMP/0001")]
    [InlineData("EMP.0001")]
    public async Task ValidateAsync_Manual_Mode_Accepts_EmployeeNumber_With_Allowed_Separators(string employeeNumber)
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row = ValidRow(2, employeeNumber: employeeNumber);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Manual_Mode_Still_Flags_Duplicate_EmployeeNumber_Within_File()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row1 = ValidRow(2, workEmail: "row1@example.com", employeeNumber: "EMP1");
        var row2 = ValidRow(3, workEmail: "row2@example.com", employeeNumber: "EMP1");

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.False(r.IsValid));
        Assert.All(results, r => Assert.Contains(r.Errors, e => e.Contains("Duplicate employee number")));
    }

    [Fact]
    public async Task ValidateAsync_Manual_Mode_Still_Flags_EmployeeNumber_Already_Existing_In_Company()
    {
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedExistingEmployeeNumber("EMP99");
        var validator = BuildValidator(
            reader,
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));
        var row = ValidRow(2, employeeNumber: "EMP99");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists in this company"));
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Accepts_Omitted_EmployeeNumber()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row = ValidRow(2, employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Rejects_Supplied_EmployeeNumber()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row = ValidRow(2, employeeNumber: "EMP-0001");

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.Contains("Employee number is auto-generated for this company and must be left blank."));
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Does_Not_Duplicate_Check_EmployeeNumber_Within_File()
    {
        // Both rows omit EmployeeNumber entirely (valid in Automatic mode) — if duplicate
        // checking still ran on the (blank) EmployeeNumber field in this mode, two blanks would
        // incorrectly be flagged as duplicates of each other.
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row1 = ValidRow(2, workEmail: "row1@example.com", employeeNumber: null);
        var row2 = ValidRow(3, workEmail: "row2@example.com", employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Does_Not_Check_EmployeeNumber_Against_Existing_Employees()
    {
        // Automatic-mode rows never carry a supplied EmployeeNumber to check (a supplied value is
        // itself an error, asserted above) — this proves lookupReader.EmployeeNumberExistsAsync is
        // never even consulted in this mode by seeding a value that would otherwise collide.
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedExistingEmployeeNumber("EMP99");
        var validator = BuildValidator(
            reader,
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row = ValidRow(2, employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        Assert.True(Assert.Single(results).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Still_Checks_WorkEmail_Duplicates_Within_File()
    {
        var validator = BuildValidator(
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row1 = ValidRow(2, workEmail: "dup@example.com", employeeNumber: null);
        var row2 = ValidRow(3, workEmail: "dup@example.com", employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row1, row2], MappedFieldsFrom(row1, row2), CancellationToken.None);

        Assert.All(results, r => Assert.False(r.IsValid));
        Assert.All(results, r => Assert.Contains(r.Errors, e => e.Contains("Duplicate work email")));
    }

    [Fact]
    public async Task ValidateAsync_Automatic_Mode_Still_Checks_WorkEmail_Against_Existing_Employees()
    {
        var reader = new FakeEmployeeImportLookupReader();
        reader.SeedExistingWorkEmail("existing@example.com");
        var validator = BuildValidator(
            reader,
            employeeNumberSettingsReader: new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));
        var row = ValidRow(2, workEmail: "existing@example.com", employeeNumber: null);

        var results = await validator.ValidateAsync(CompanyId, [row], MappedFieldsFrom(row), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists in this company"));
    }
}
