using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

public class NotificationChannelDefaultsTests
{
    [Theory]
    [InlineData(NotificationType.LeaveApproved)]
    [InlineData(NotificationType.LeaveRejected)]
    [InlineData(NotificationType.ProbationOutcomeRecorded)]
    [InlineData(NotificationType.ProbationReviewDue)]
    [InlineData(NotificationType.OffboardingRequiresHrReconciliation)]
    [InlineData(NotificationType.IncompleteOffboardingAtDeparture)]
    [InlineData(NotificationType.DocumentExpired)]
    [InlineData(NotificationType.SicknessEvidenceOverdue)]
    [InlineData(NotificationType.ReturnToWorkReviewOverdue)]
    public void GetChannel_Returns_Both_For_Email_Eligible_Types(NotificationType type)
    {
        var channel = NotificationChannelDefaults.GetChannel(type);

        Assert.Equal(NotificationChannel.Both, channel);
        Assert.True(channel.HasFlag(NotificationChannel.Email));
        Assert.True(channel.HasFlag(NotificationChannel.InApp));
    }

    [Theory]
    [InlineData(NotificationType.TaskAssigned)]
    [InlineData(NotificationType.TaskDueSoon)]
    public void GetChannel_Returns_InApp_Only_For_Non_Email_Eligible_Types(NotificationType type)
    {
        var channel = NotificationChannelDefaults.GetChannel(type);

        Assert.Equal(NotificationChannel.InApp, channel);
        Assert.False(channel.HasFlag(NotificationChannel.Email));
    }

    // SET-06 -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(NotificationType.DocumentExpired)]
    [InlineData(NotificationType.SicknessEvidenceOverdue)]
    [InlineData(NotificationType.ReturnToWorkReviewOverdue)]
    public void IsMandatoryEmail_Returns_True_For_The_Documented_Mandatory_Types(NotificationType type)
    {
        Assert.True(NotificationChannelDefaults.IsMandatoryEmail(type));
    }

    [Theory]
    [InlineData(NotificationType.LeaveApproved)]
    [InlineData(NotificationType.LeaveRejected)]
    [InlineData(NotificationType.ProbationOutcomeRecorded)]
    [InlineData(NotificationType.ProbationReviewDue)]
    [InlineData(NotificationType.OffboardingRequiresHrReconciliation)]
    [InlineData(NotificationType.IncompleteOffboardingAtDeparture)]
    [InlineData(NotificationType.TaskAssigned)]
    [InlineData(NotificationType.TaskDueSoon)]
    public void IsMandatoryEmail_Returns_False_For_Other_Types_Including_Other_Email_Eligible_Types(NotificationType type)
    {
        Assert.False(NotificationChannelDefaults.IsMandatoryEmail(type));
    }

    [Theory]
    [InlineData(NotificationType.TaskDueSoon)]
    [InlineData(NotificationType.DocumentExpiring)]
    [InlineData(NotificationType.AssetAcknowledgementReminder)]
    [InlineData(NotificationType.AssetReturnReminder)]
    [InlineData(NotificationType.SicknessEvidenceReminder)]
    [InlineData(NotificationType.ReturnToWorkReviewReminder)]
    [InlineData(NotificationType.InterviewReminder)]
    [InlineData(NotificationType.SharedCompanyDocumentAcknowledgementReminder)]
    public void IsScheduledReminder_Returns_True_For_The_Documented_Reminder_Types(NotificationType type)
    {
        Assert.True(NotificationChannelDefaults.IsScheduledReminder(type));
    }

    [Theory]
    [InlineData(NotificationType.TaskOverdue)]
    [InlineData(NotificationType.DocumentExpired)]
    [InlineData(NotificationType.SicknessEvidenceOverdue)]
    [InlineData(NotificationType.TaskAssigned)]
    public void IsScheduledReminder_Returns_False_For_Overdue_Escalation_And_NonReminder_Types(NotificationType type)
    {
        Assert.False(NotificationChannelDefaults.IsScheduledReminder(type));
    }
}
