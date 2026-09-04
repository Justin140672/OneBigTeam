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

    // Spot-check enum member names from EqualityEnums.cs — none may appear in a serialized payload.
    private static readonly string[] EnumMemberNames =
        ["Christian", "GayOrLesbian", "Woman", "White", "Muslim", "Bisexual", "NonBinary",
         "AsianOrAsianBritish", "PreferNotToSay", "SelfDescribed"];

    [Fact]
    public void UpdatedAuditEvent_Serialized_As_AuditPendingItem_Does_Contains_No_Enum_Names_Or_FreeText()
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

        // Serialize Before/After/Metadata exactly as AuditPendingItem.From does.
        var before = evt.Before is null ? null : JsonSerializer.Serialize(evt.Before);
        var after = evt.After is null ? null : JsonSerializer.Serialize(evt.After);
        var metadata = evt.Metadata is null ? null : JsonSerializer.Serialize(evt.Metadata);
        var combined = string.Join("|", before, after, metadata, evt.Summary);

        Assert.Null(before);
        Assert.Null(metadata);
        foreach (var name in EnumMemberNames)
            Assert.DoesNotContain(name, combined, StringComparison.Ordinal);

        // After carries only the six presence flags plus Created — all booleans, no strings.
        using var doc = JsonDocument.Parse(after!);
        foreach (var prop in doc.RootElement.EnumerateObject())
            Assert.Contains(prop.Value.ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
    }

    [Theory]
    [InlineData(true, "Equality monitoring data provided")]
    [InlineData(false, "Equality monitoring data updated")]
    public void UpdatedAuditEvent_Summary_Is_A_Fixed_Action_Level_String(bool created, string expected)
    {
        IAuditEvent evt = new EqualityDataUpdatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), created,
            true, true, true, true, true, true, DateTimeOffset.UtcNow);

        Assert.Equal(expected, evt.Summary);
    }

    [Fact]
    public void DeletedAuditEvent_Summary_Is_Withdrawn()
    {
        IAuditEvent evt = new EqualityDataDeletedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal("Equality monitoring data withdrawn", evt.Summary);
    }

    [Fact]
    public void UpdatedAuditEvent_Actor_Is_The_Subject_Employee()
    {
        var employeeId = Guid.NewGuid();
        IAuditEvent evt = new EqualityDataUpdatedAuditEvent(
            Guid.NewGuid(), employeeId, Guid.NewGuid(), true,
            true, true, true, true, true, true, DateTimeOffset.UtcNow);

        Assert.Equal(employeeId, evt.EmployeeId);
        Assert.Equal(employeeId, evt.ActorEmployeeId);
        Assert.Equal("employee.equality_data.updated", evt.EventType);
    }

    [Fact]
    public void DeletedAuditEvent_Actor_Is_The_Subject_Employee()
    {
        var employeeId = Guid.NewGuid();
        IAuditEvent evt = new EqualityDataDeletedAuditEvent(
            Guid.NewGuid(), employeeId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(employeeId, evt.EmployeeId);
        Assert.Equal(employeeId, evt.ActorEmployeeId);
        Assert.Equal("employee.equality_data.deleted", evt.EventType);
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
