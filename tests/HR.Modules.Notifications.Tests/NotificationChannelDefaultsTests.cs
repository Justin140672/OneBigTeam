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
}
