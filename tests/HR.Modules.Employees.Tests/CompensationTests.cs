using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class CompensationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculateAnnualisedSalary_Returns_Salary_Directly_For_Annual_Type()
    {
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Annual, 45000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);

        Assert.Equal(45000m, record.CalculateAnnualisedSalary());
    }

    [Fact]
    public void CalculateAnnualisedSalary_Multiplies_Rate_By_HoursPerWeek_And_52_For_Hourly_Type()
    {
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Hourly, 25m, "GBP", 37.5m, 1m, null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);

        Assert.Equal(25m * 37.5m * 52, record.CalculateAnnualisedSalary());
    }

    [Fact]
    public void CalculateAnnualisedSalary_Returns_Null_For_Hourly_Type_Without_HoursPerWeek()
    {
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Hourly, 25m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);

        Assert.Null(record.CalculateAnnualisedSalary());
    }

    [Fact]
    public void CalculateAnnualisedSalary_Multiplies_Rate_By_260_For_Daily_Type()
    {
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Daily, 200m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);

        Assert.Equal(200m * 260, record.CalculateAnnualisedSalary());
    }

    [Fact]
    public void Create_Sets_Reason_And_CreatedBy()
    {
        var createdBy = Guid.NewGuid();
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Annual, 45000m, "GBP", null, null, null, CompensationChangeReason.Promotion, createdBy, Now);

        Assert.Equal(CompensationChangeReason.Promotion, record.Reason);
        Assert.Equal(createdBy, record.CreatedBy);
    }

    [Fact]
    public void Update_Changes_Reason_But_Not_CreatedBy()
    {
        var createdBy = Guid.NewGuid();
        var record = Compensation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1),
            SalaryType.Annual, 45000m, "GBP", null, null, null, CompensationChangeReason.NewHire, createdBy, Now);

        record.Update(SalaryType.Annual, 46000m, "GBP", null, null, null, CompensationChangeReason.Correction, Now);

        Assert.Equal(CompensationChangeReason.Correction, record.Reason);
        Assert.Equal(createdBy, record.CreatedBy);
    }
}
