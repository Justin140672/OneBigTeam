using System.Text.Json;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// NFR-01: the compensation audit events must never serialise a monetary amount. The bulk-applied
/// event records only the direction of the change; the other compensation events record no amount
/// at all.
/// </summary>
public class CompensationAuditRedactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 1);

    private static string SerializeAfter(IAuditEvent evt) => JsonSerializer.Serialize(evt.After);
    private static string SerializeAll(IAuditEvent evt) =>
        JsonSerializer.Serialize(new { evt.Before, evt.After, evt.Metadata });

    [Theory]
    [InlineData(60000, 50000, "Increase")]
    [InlineData(50000, 60000, "Decrease")]
    [InlineData(55000, 55000, "NoChange")]
    public void BulkApplied_records_direction_and_never_the_amount(decimal salary, decimal previousSalary, string expectedDirection)
    {
        var evt = new CompensationRecordBulkAppliedAuditEvent(
            CompanyId: Guid.NewGuid(),
            EmployeeId: Guid.NewGuid(),
            CompensationRecordId: Guid.NewGuid(),
            ActorEmployeeId: Guid.NewGuid(),
            EffectiveFrom: EffectiveFrom,
            SalaryType: "Annual",
            Salary: salary,
            PreviousSalary: previousSalary,
            Currency: "GBP",
            Reason: "Annual review",
            AdjustmentMode: "PercentageIncrease",
            BulkOperationId: Guid.NewGuid(),
            OccurredAt: Now);

        var json = SerializeAll(evt);

        Assert.Contains(expectedDirection, SerializeAfter(evt));
        Assert.DoesNotContain("60000", json);
        Assert.DoesNotContain("50000", json);
        Assert.DoesNotContain("55000", json);
        Assert.DoesNotContain("Salary\":", json);          // no "Salary" / "PreviousSalary" amount property
        Assert.DoesNotContain("Annual review", json);       // free-text reason excluded
    }

    [Fact]
    public void Created_event_serialises_no_salary_amount()
    {
        var evt = new CompensationRecordCreatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EffectiveFrom, "Annual", Salary: 55000m, "GBP", Reason: "New hire", Now);

        var json = SerializeAll(evt);
        Assert.DoesNotContain("55000", json);
        Assert.DoesNotContain("New hire", json);
        Assert.Contains("Annual", SerializeAfter(evt));
        Assert.Contains("GBP", SerializeAfter(evt));
    }

    [Fact]
    public void Updated_event_serialises_no_salary_amount()
    {
        var evt = new CompensationRecordUpdatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EffectiveFrom, "Annual", Salary: 55000m, "GBP", Reason: "Correction", Now);

        var json = SerializeAll(evt);
        Assert.DoesNotContain("55000", json);
        Assert.DoesNotContain("Correction", json);
    }

    [Fact]
    public void Imported_event_serialises_no_salary_amount()
    {
        var evt = new CompensationRecordImportedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EffectiveFrom, "Annual", Salary: 55000m, "GBP", Reason: "Import", ImportBatchId: Guid.NewGuid(), Now);

        var json = SerializeAll(evt);
        Assert.DoesNotContain("55000", json);
    }

    /// <summary>Belt-and-braces: none of the compensation payloads trip the value scrubber either.</summary>
    [Fact]
    public void No_compensation_payload_contains_a_sensitive_value_token()
    {
        var events = new IAuditEvent[]
        {
            new CompensationRecordCreatedAuditEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                EffectiveFrom, "Annual", 55000m, "GBP", "New hire", Now),
            new CompensationRecordUpdatedAuditEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                EffectiveFrom, "Annual", 55000m, "GBP", "Correction", Now),
            new CompensationRecordImportedAuditEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                EffectiveFrom, "Annual", 55000m, "GBP", "Import", Guid.NewGuid(), Now),
        };

        foreach (var evt in events)
            Assert.False(SensitiveDataScrubber.ContainsSensitiveValue(SerializeAll(evt)));
    }
}
