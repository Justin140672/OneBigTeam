using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Services;

namespace HR.Modules.Probation.Tests;

public class ProbationReviewAssignmentTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 4, 1);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolveTaskAssignee_Returns_Manager_For_ManagerCheckIn() =>
        AssertResolveTaskAssigneeReturnsManager(ProbationReviewType.ManagerCheckIn);

    [Fact]
    public void ResolveTaskAssignee_Returns_Manager_For_FinalDecision() =>
        AssertResolveTaskAssigneeReturnsManager(ProbationReviewType.FinalDecision);

    [Fact]
    public void ResolveTaskAssignee_Returns_Manager_For_ExtensionConfirmation() =>
        AssertResolveTaskAssigneeReturnsManager(ProbationReviewType.ExtensionConfirmation);

    private static void AssertResolveTaskAssigneeReturnsManager(ProbationReviewType reviewType)
    {
        var managerId = Guid.NewGuid();
        var record = CreateRecord(managerId);

        var assignee = ProbationReviewAssignment.ResolveTaskAssignee(record, reviewType, []);

        Assert.Equal(managerId, assignee);
    }

    [Fact]
    public void ResolveTaskAssignee_HrReview_Returns_Lowest_Guid_When_Multiple_Hr_Admins()
    {
        var record = CreateRecord(Guid.NewGuid());
        var lowest = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higher = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var middle = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var assignee = ProbationReviewAssignment.ResolveTaskAssignee(
            record, ProbationReviewType.HrReview, [higher, lowest, middle]);

        Assert.Equal(lowest, assignee);
    }

    [Fact]
    public void ResolveTaskAssignee_HrReview_Returns_Null_When_No_Hr_Admins()
    {
        var record = CreateRecord(Guid.NewGuid());

        var assignee = ProbationReviewAssignment.ResolveTaskAssignee(
            record, ProbationReviewType.HrReview, []);

        Assert.Null(assignee);
    }

    [Fact]
    public void ResolveNotificationRecipients_HrReview_Returns_Full_Hr_Admin_List()
    {
        var record = CreateRecord(Guid.NewGuid());
        var admin1 = Guid.NewGuid();
        var admin2 = Guid.NewGuid();
        var admin3 = Guid.NewGuid();

        var recipients = ProbationReviewAssignment.ResolveNotificationRecipients(
            record, ProbationReviewType.HrReview, [admin1, admin2, admin3]);

        Assert.Equal(3, recipients.Count);
        Assert.Contains(admin1, recipients);
        Assert.Contains(admin2, recipients);
        Assert.Contains(admin3, recipients);
    }

    [Fact]
    public void ResolveNotificationRecipients_HrReview_Returns_Empty_When_No_Hr_Admins()
    {
        var record = CreateRecord(Guid.NewGuid());

        var recipients = ProbationReviewAssignment.ResolveNotificationRecipients(
            record, ProbationReviewType.HrReview, []);

        Assert.Empty(recipients);
    }

    [Fact]
    public void ResolveNotificationRecipients_Returns_Only_Manager_For_ManagerCheckIn() =>
        AssertResolveNotificationRecipientsReturnsOnlyManager(ProbationReviewType.ManagerCheckIn);

    [Fact]
    public void ResolveNotificationRecipients_Returns_Only_Manager_For_FinalDecision() =>
        AssertResolveNotificationRecipientsReturnsOnlyManager(ProbationReviewType.FinalDecision);

    [Fact]
    public void ResolveNotificationRecipients_Returns_Only_Manager_For_ExtensionConfirmation() =>
        AssertResolveNotificationRecipientsReturnsOnlyManager(ProbationReviewType.ExtensionConfirmation);

    private static void AssertResolveNotificationRecipientsReturnsOnlyManager(ProbationReviewType reviewType)
    {
        var managerId = Guid.NewGuid();
        var record = CreateRecord(managerId);

        var recipients = ProbationReviewAssignment.ResolveNotificationRecipients(
            record, reviewType, [Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Equal([managerId], recipients);
    }

    private static ProbationRecord CreateRecord(Guid managerEmployeeId) =>
        ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), managerEmployeeId,
            StartDate, ExpectedEndDate, null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
}
