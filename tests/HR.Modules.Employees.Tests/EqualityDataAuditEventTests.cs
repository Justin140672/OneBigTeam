using System.Text.Json;
using HR.Modules.Employees;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Equality monitoring answers are special-category data — audit payloads must never carry an
/// answer value, only ids, timestamps and boolean "was X provided" flags.
/// </summary>
public class EqualityDataAuditEventTests
{
    // Strings that would appear if any answer value leaked into a payload.
    private static readonly string[] AnswerLikeTokens =
    [
        "White", "Mixed", "SelfDescribed", "Woman", "Man", "Bisexual", "GayOrLesbian",
        "Muslim", "Christian", "NoReligion", "Cornish", "Chronic fatigue", "Yes", "No"
    ];

    [Fact]
    public void UpdatedAuditEvent_Payload_Contains_No_Answer_Values()
    {
        IAuditEvent evt = new EqualityDataUpdatedAuditEvent(
            CompanyId: Guid.NewGuid(),
            EmployeeId: Guid.NewGuid(),
            RecordId: Guid.NewGuid(),
            Created: true,
            GenderIdentityProvided: true,
            MarriedOrCivilPartnershipStatusProvided: true,
            EthnicGroupProvided: true,
            DisabilityStatusProvided: true,
            SexualOrientationProvided: true,
            ReligionOrBeliefProvided: true,
            OccurredAt: DateTimeOffset.UtcNow);

        AssertNoAnswerValues(evt);
        Assert.Equal("employee.equality_data.updated", evt.EventType);
        Assert.Equal("EmployeeEqualityData", evt.EntityType);
        Assert.Null(evt.Before);
        Assert.Equal("Equality monitoring data provided", evt.Summary);

        // The After payload is only presence flags.
        var after = JsonSerializer.Serialize(evt.After);
        Assert.Contains("Provided", after);
        Assert.Contains("Created", after);
    }

    [Fact]
    public void UpdatedAuditEvent_Summary_Reflects_Update_When_Not_Created()
    {
        IAuditEvent evt = new EqualityDataUpdatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Created: false,
            false, false, false, false, false, false,
            DateTimeOffset.UtcNow);

        Assert.Equal("Equality monitoring data updated", evt.Summary);
        AssertNoAnswerValues(evt);
    }

    [Fact]
    public void DeletedAuditEvent_Payload_Contains_No_Answer_Values()
    {
        IAuditEvent evt = new EqualityDataDeletedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        AssertNoAnswerValues(evt);
        Assert.Equal("employee.equality_data.deleted", evt.EventType);
        Assert.Null(evt.Before);
        Assert.Null(evt.After);
        Assert.Equal("Equality monitoring data withdrawn", evt.Summary);
    }

    private static void AssertNoAnswerValues(IAuditEvent evt)
    {
        var serialized = string.Join(
            "|",
            JsonSerializer.Serialize(evt.Before),
            JsonSerializer.Serialize(evt.After),
            JsonSerializer.Serialize(evt.Metadata),
            evt.Summary ?? string.Empty);

        foreach (var token in AnswerLikeTokens)
            Assert.DoesNotContain(token, serialized, StringComparison.Ordinal);
    }
}
