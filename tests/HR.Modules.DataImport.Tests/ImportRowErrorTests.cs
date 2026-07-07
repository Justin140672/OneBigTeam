using HR.Modules.DataImport.Domain;

namespace HR.Modules.DataImport.Tests;

public class ImportRowErrorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Fields_Correctly_With_RawRowData()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var importSessionId = Guid.NewGuid();
        const string rawRowData = "John,Doe,john.doe@example.com";

        var rowError = ImportRowError.Create(
            id,
            companyId,
            importSessionId,
            rowNumber: 42,
            ImportRowErrorSeverity.Error,
            "Email already in use",
            rawRowData,
            FixedNow);

        Assert.Equal(id, rowError.Id);
        Assert.Equal(companyId, rowError.CompanyId);
        Assert.Equal(importSessionId, rowError.ImportSessionId);
        Assert.Equal(42, rowError.RowNumber);
        Assert.Equal(ImportRowErrorSeverity.Error, rowError.Severity);
        Assert.Equal("Email already in use", rowError.ErrorMessage);
        Assert.Equal(rawRowData, rowError.RawRowData);
        Assert.Equal(FixedNow, rowError.CreatedAt);
    }

    [Fact]
    public void Create_Allows_Null_RawRowData()
    {
        var rowError = ImportRowError.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            rowNumber: 7,
            ImportRowErrorSeverity.Warning,
            "Optional field missing",
            rawRowData: null,
            FixedNow);

        Assert.Null(rowError.RawRowData);
        Assert.Equal(ImportRowErrorSeverity.Warning, rowError.Severity);
        Assert.Equal("Optional field missing", rowError.ErrorMessage);
        Assert.Equal(7, rowError.RowNumber);
    }

    [Fact]
    public void Create_Sets_Severity_As_Provided_Warning()
    {
        AssertSeverityIsSetAsProvided(ImportRowErrorSeverity.Warning);
    }

    [Fact]
    public void Create_Sets_Severity_As_Provided_Error()
    {
        AssertSeverityIsSetAsProvided(ImportRowErrorSeverity.Error);
    }

    private static void AssertSeverityIsSetAsProvided(ImportRowErrorSeverity severity)
    {
        var rowError = ImportRowError.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            rowNumber: 1,
            severity,
            "Some issue",
            rawRowData: null,
            FixedNow);

        Assert.Equal(severity, rowError.Severity);
    }
}
