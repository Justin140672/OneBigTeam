namespace HR.SharedKernel;

public enum NotificationType
{
    TaskAssigned     = 1,
    TaskDueSoon      = 2,
    TaskOverdue      = 3,
    LeaveApproved    = 4,
    LeaveRejected    = 5,
    LeaveRequested   = 6,
    DocumentExpiring = 7,
    DocumentExpired  = 8,
    TaskCompleted         = 9,
    AssetAssigned         = 10,
    AssetReturnRequested  = 11,
    AssetAcknowledgementReminder = 12,
    AssetReturnReminder          = 13,
    AssetAcknowledgementOverdue  = 14,
    AssetReturnOverdue           = 15,
    SicknessRecorded             = 16,
}
