using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

public class NotificationEventMappingContractTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid SourceEntityId = Guid.NewGuid();

    private static readonly HashSet<NotificationType> ExpectedEmailEligibleTypes =
    [
        NotificationType.LeaveApproved,
        NotificationType.LeaveRejected,
        NotificationType.ProbationOutcomeRecorded,
        NotificationType.ProbationReviewDue,
        NotificationType.OffboardingRequiresHrReconciliation,
        NotificationType.IncompleteOffboardingAtDeparture,
        NotificationType.DocumentExpired,
        NotificationType.SicknessEvidenceOverdue,
        NotificationType.ReturnToWorkReviewOverdue,
    ];

    private static readonly HashSet<NotificationType> ExpectedTemplateBackedTypes =
    [
        NotificationType.LeaveRequested,
        NotificationType.LeaveApproved,
        NotificationType.EmployeeCreated,
        NotificationType.CandidateHired,
        NotificationType.DocumentExpiring,
        NotificationType.TaskAssigned,
    ];

    // NotificationTypes NotificationActionRouteBuilder deliberately returns null for — no
    // interview-detail route exists in HR.Web today (see that class's doc comment). Every other type
    // is expected to resolve a non-null, application-relative action URL.
    private static readonly HashSet<NotificationType> ExpectedActionlessTypes =
    [
        NotificationType.InterviewScheduled,
        NotificationType.InterviewFeedbackOverdue,
        NotificationType.InterviewReminder,

        // Customer Release Notifications: ProductUpdate's ActionUrl is an admin-supplied explicit
        // override passed to INotificationWriter.WriteAsync at send time (see SendProductUpdateHandler),
        // not something NotificationActionRouteBuilder can derive from type/company/employee/source
        // ids alone — so BuildActionUrl deliberately returns null for this type.
        NotificationType.ProductUpdate,

        // InternalOfferResponded: the vacancy-applications destination needs the vacancy id, which the
        // writer does not receive, so the sender passes an explicit ActionUrl to WriteAsync instead.
        NotificationType.InternalOfferResponded,
    ];

    public static IEnumerable<object[]> AllNotificationTypes() =>
        Enum.GetValues<NotificationType>().Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(AllNotificationTypes))]
    public void Every_NotificationType_Has_An_Explicit_Channel_Expectation(NotificationType type)
    {
        var expectedChannel = ExpectedEmailEligibleTypes.Contains(type)
            ? NotificationChannel.Both
            : NotificationChannel.InApp;

        Assert.Equal(expectedChannel, NotificationChannelDefaults.GetChannel(type));
    }

    [Theory]
    [MemberData(nameof(AllNotificationTypes))]
    public void Every_NotificationType_Has_An_Explicit_Template_Expectation(NotificationType type)
    {
        var expectedHasTemplate = ExpectedTemplateBackedTypes.Contains(type);
        var actualHasTemplate = NotificationTemplateCatalogue.TryGet(type, out _);

        Assert.Equal(expectedHasTemplate, actualHasTemplate);
    }

    [Theory]
    [MemberData(nameof(AllNotificationTypes))]
    public void Every_NotificationType_Has_An_Explicit_Action_Route_Expectation(NotificationType type)
    {
        var expectedHasRoute = !ExpectedActionlessTypes.Contains(type);
        var actualUrl = NotificationActionRouteBuilder.BuildActionUrl(type, CompanyId, EmployeeId, SourceEntityId);

        Assert.Equal(expectedHasRoute, actualUrl is not null);
    }

    [Fact]
    public void Every_Template_Backed_Type_Also_Has_An_Action_Route()
    {
        var templateBackedWithoutRoute = ExpectedTemplateBackedTypes
            .Where(type => NotificationActionRouteBuilder.BuildActionUrl(type, CompanyId, EmployeeId, SourceEntityId) is null)
            .ToList();

        Assert.Empty(templateBackedWithoutRoute);
    }
}
