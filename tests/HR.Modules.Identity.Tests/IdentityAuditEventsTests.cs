using HR.SharedKernel;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: <see cref="IAuditEvent"/> property-mapping tests for the two new audit events
/// (<see cref="PermissionDeniedAuditEvent"/> and <see cref="AccessReviewExportedAuditEvent"/>)
/// introduced in IdentityAudit.cs.
/// </summary>
public class IdentityAuditEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    // -----------------------------------------------------------------------
    // PermissionDeniedAuditEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void PermissionDeniedAuditEvent_Maps_Core_Properties()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        IAuditEvent auditEvent = new PermissionDeniedAuditEvent(
            companyId, userId, permissionId, DenialCountInWindow: 1, IsRepeatedEscalation: false, Now);

        Assert.Equal("user.permission-denied", auditEvent.EventType);
        Assert.Equal("ApplicationUser", auditEvent.EntityType);
        Assert.Equal(userId, auditEvent.EntityId);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(userId, auditEvent.EmployeeId);
        Assert.Equal(userId, auditEvent.ActorUserId);
        Assert.Null(auditEvent.ActorEmployeeId);
        Assert.Null(auditEvent.CorrelationId);
        Assert.Null(auditEvent.Before);
        Assert.Null(auditEvent.After);
    }

    [Fact]
    public void PermissionDeniedAuditEvent_Summary_Is_The_Plain_Denial_Message_When_Not_An_Escalation()
    {
        var permissionId = Guid.NewGuid();
        IAuditEvent auditEvent = new PermissionDeniedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), permissionId, DenialCountInWindow: 1, IsRepeatedEscalation: false, Now);

        Assert.Equal($"Permission denied: {permissionId}", auditEvent.Summary);
    }

    [Fact]
    public void PermissionDeniedAuditEvent_Summary_Describes_The_Repeated_Escalation_When_IsRepeatedEscalation_Is_True()
    {
        var permissionId = Guid.NewGuid();
        IAuditEvent auditEvent = new PermissionDeniedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), permissionId, DenialCountInWindow: 5, IsRepeatedEscalation: true, Now);

        Assert.Equal(
            $"Repeated permission denial (5 in the last 15 minutes) for permission {permissionId}",
            auditEvent.Summary);
    }

    [Fact]
    public void PermissionDeniedAuditEvent_Metadata_Carries_No_Sensitive_Payload_Only_Ids_And_Counts()
    {
        var permissionId = Guid.NewGuid();
        IAuditEvent auditEvent = new PermissionDeniedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), permissionId, DenialCountInWindow: 3, IsRepeatedEscalation: false, Now);

        var metadata = Assert.IsType<object>(auditEvent.Metadata, exactMatch: false);
        Assert.Contains("PermissionId", metadata.ToString());
        Assert.Contains("DenialCountInWindow", metadata.ToString());
        Assert.Contains("IsRepeatedEscalation", metadata.ToString());
    }

    // -----------------------------------------------------------------------
    // AccessReviewExportedAuditEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void AccessReviewExportedAuditEvent_Maps_Core_Properties()
    {
        var companyId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();

        IAuditEvent auditEvent = new AccessReviewExportedAuditEvent(
            companyId, "Csv", Success: true, RowCount: 12, FailureReason: null, actorUserId, Now);

        Assert.Equal("access-review.exported", auditEvent.EventType);
        Assert.Equal("AccessReviewReport", auditEvent.EntityType);
        Assert.Equal(companyId, auditEvent.EntityId);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Null(auditEvent.ActorEmployeeId);
        Assert.Null(auditEvent.CorrelationId);
        Assert.Null(auditEvent.Before);
        Assert.Null(auditEvent.After);
    }

    [Fact]
    public void AccessReviewExportedAuditEvent_Summary_Reports_Success_With_Format_And_Row_Count()
    {
        IAuditEvent auditEvent = new AccessReviewExportedAuditEvent(
            Guid.NewGuid(), "Excel", Success: true, RowCount: 42, FailureReason: null, Guid.NewGuid(), Now);

        Assert.Equal("Exported access-review report (Excel, 42 rows)", auditEvent.Summary);
    }

    [Fact]
    public void AccessReviewExportedAuditEvent_Summary_Reports_Failure_With_Format_And_Reason_When_Success_Is_False()
    {
        IAuditEvent auditEvent = new AccessReviewExportedAuditEvent(
            Guid.NewGuid(), "Pdf", Success: false, RowCount: null, FailureReason: "Renderer unavailable", Guid.NewGuid(), Now);

        Assert.Equal("Access-review report export failed (Pdf): Renderer unavailable", auditEvent.Summary);
    }

    [Fact]
    public void AccessReviewExportedAuditEvent_Never_Carries_The_Actual_Exported_Rows()
    {
        // Metadata must only ever carry Format/RowCount/Success/FailureReason — never the row data
        // itself (see the field-level remark in IdentityAudit.cs).
        IAuditEvent auditEvent = new AccessReviewExportedAuditEvent(
            Guid.NewGuid(), "Csv", Success: true, RowCount: 3, FailureReason: null, Guid.NewGuid(), Now);

        var metadata = auditEvent.Metadata!.ToString()!;
        Assert.Contains("Format", metadata);
        Assert.Contains("RowCount", metadata);
        Assert.Contains("Success", metadata);
        Assert.Contains("FailureReason", metadata);
    }
}
