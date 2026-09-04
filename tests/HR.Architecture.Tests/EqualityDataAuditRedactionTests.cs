using System.Text.Json;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees;
using HR.SharedKernel;

namespace HR.Architecture.Tests;

/// <summary>
/// Ticket 3: voluntary equality-monitoring data is special-category data. Its audit events carry
/// only ids, timestamps, fixed action-level summaries and boolean "&lt;field&gt;Provided" flags — so
/// the NFR-01 <see cref="AuditPayloadRedactionGuard"/> must accept them (they must not be dropped),
/// and no answer value / enum member name / ciphertext must appear in the serialized payload.
/// </summary>
public class EqualityDataAuditRedactionTests
{
    private static readonly string[] EnumMemberNames =
        ["Christian", "GayOrLesbian", "Woman", "White", "Muslim", "Bisexual", "NonBinary", "SelfDescribed"];

    private static (string? Before, string? After, string? Metadata) Serialize(IAuditEvent evt) =>
    (
        evt.Before is null ? null : JsonSerializer.Serialize(evt.Before),
        evt.After is null ? null : JsonSerializer.Serialize(evt.After),
        evt.Metadata is null ? null : JsonSerializer.Serialize(evt.Metadata)
    );

    public static IEnumerable<object[]> EqualityAuditEvents()
    {
        yield return [new EqualityDataUpdatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Created: true, true, true, true, true, true, true, DateTimeOffset.UtcNow)];
        yield return [new EqualityDataUpdatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Created: false, false, false, false, false, false, false, DateTimeOffset.UtcNow)];
        yield return [new EqualityDataDeletedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow)];
    }

    [Theory]
    [MemberData(nameof(EqualityAuditEvents))]
    public void Redaction_guard_accepts_equality_audit_payloads(IAuditEvent evt)
    {
        var (before, after, metadata) = Serialize(evt);

        AuditPayloadRedactionGuard.AssertPayloadIsSafe(before, "Before");
        AuditPayloadRedactionGuard.AssertPayloadIsSafe(after, "After");
        AuditPayloadRedactionGuard.AssertPayloadIsSafe(metadata, "Metadata");
    }

    [Theory]
    [MemberData(nameof(EqualityAuditEvents))]
    public void Equality_audit_payloads_carry_no_answer_values(IAuditEvent evt)
    {
        var (before, after, metadata) = Serialize(evt);
        var combined = string.Join("|", before, after, metadata, evt.Summary);

        foreach (var name in EnumMemberNames)
            Assert.DoesNotContain(name, combined, StringComparison.Ordinal);

        Assert.DoesNotContain("OBTENC1:", combined, StringComparison.Ordinal);
    }
}
