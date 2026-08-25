using HR.Modules.Notifications.Domain;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests;

// NOT-04: NotificationActionRouteBuilder — per-NotificationType navigation target computation.
public class NotificationActionRouteBuilderTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid SourceEntityId = Guid.NewGuid();

    [Fact]
    public void TaskAssigned_Resolves_To_Task_Detail_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.TaskAssigned, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/tasks/{SourceEntityId}", url);
    }

    [Fact]
    public void LeaveApproved_Resolves_To_Employee_Leave_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.LeaveApproved, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=leave", url);
    }

    [Fact]
    public void AssetAssigned_Resolves_To_Asset_View_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.AssetAssigned, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/assets/{SourceEntityId}/view", url);
    }

    [Fact]
    public void SicknessRecorded_Resolves_To_Employee_Sickness_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.SicknessRecorded, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=sickness", url);
    }

    [Fact]
    public void DocumentExpiring_Resolves_To_Employee_Documents_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.DocumentExpiring, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=documents", url);
    }

    [Fact]
    public void SharedCompanyDocumentReviewDue_Resolves_To_Shared_Document_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.SharedCompanyDocumentReviewDue, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/shared-documents/{SourceEntityId}", url);
    }

    [Fact]
    public void ProbationReviewDue_Resolves_To_Employee_Probation_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.ProbationReviewDue, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=probation", url);
    }

    [Fact]
    public void OnboardingStarted_Resolves_To_Employee_Onboarding_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.OnboardingStarted, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=onboarding", url);
    }

    [Fact]
    public void OffboardingStarted_Resolves_To_Employee_Offboarding_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.OffboardingStarted, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=offboarding", url);
    }

    [Fact]
    public void LeavingProcessStarted_Resolves_To_Employee_Leaving_Tab()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.LeavingProcessStarted, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}?tab=leaving", url);
    }

    [Fact]
    public void EmployeeCreated_Resolves_To_Employee_Detail_Route_Keyed_By_SourceEntityId()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.EmployeeCreated, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{SourceEntityId}", url);
    }

    [Fact]
    public void CandidateHired_Resolves_To_Candidate_Detail_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.CandidateHired, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/candidates/{SourceEntityId}", url);
    }

    [Fact]
    public void SupportRequestStatusChanged_Resolves_To_Support_Detail_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.SupportRequestStatusChanged, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/support/{SourceEntityId}", url);
    }

    [Fact]
    public void ProfilePhotoApproved_Resolves_To_Employee_Profile_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.ProfilePhotoApproved, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}/profile", url);
    }

    [Fact]
    public void ProfilePhotoRejected_Resolves_To_Employee_Profile_Route()
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.ProfilePhotoRejected, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal($"/companies/{CompanyId}/employees/{EmployeeId}/profile", url);
    }

    [Theory]
    [InlineData(NotificationType.InterviewScheduled)]
    [InlineData(NotificationType.InterviewFeedbackOverdue)]
    [InlineData(NotificationType.InterviewReminder)]
    public void Interview_Types_Have_No_Destination_And_Return_Null(NotificationType type)
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(type, CompanyId, EmployeeId, SourceEntityId);

        Assert.Null(url);
    }

    [Theory]
    [MemberData(nameof(AllNotificationTypes))]
    public void BuildActionUrl_Returns_Null_Or_A_Safe_Application_Relative_Url_For_Every_Type(NotificationType type)
    {
        var url = NotificationActionRouteBuilder.BuildActionUrl(type, CompanyId, EmployeeId, SourceEntityId);

        if (url is null)
            return;

        Assert.StartsWith("/", url);
        Assert.False(url.StartsWith("//"));
        Assert.False(url.StartsWith("http://"));
        Assert.False(url.StartsWith("https://"));
    }

    [Fact]
    public void BuildActionUrl_Is_Deterministic_For_The_Same_Inputs()
    {
        var first = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.TaskAssigned, CompanyId, EmployeeId, SourceEntityId);
        var second = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.TaskAssigned, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal(first, second);
    }

    public static IEnumerable<object[]> AllNotificationTypes()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            yield return new object[] { type };
        }
    }
}
