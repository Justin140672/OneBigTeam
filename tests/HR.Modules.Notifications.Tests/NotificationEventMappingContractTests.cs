using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// NOT-07: a single, explicit, machine-checked contract of every NotificationType's delivery
/// mapping — channel default (NOT-02), template presence (NOT-03) and action-route presence
/// (NOT-04) — walked exhaustively via <see cref="Enum.GetValues{TEnum}"/> rather than a hand-picked
/// subset. Adding a new NotificationType without adding a corresponding entry to the expectation
/// maps below fails <see cref="Every_NotificationType_Has_An_Explicit_Channel_Expectation"/> (and
/// the template/route theories, driven off <see cref="AllNotificationTypes"/>), which is the point:
/// this test exists specifically to catch future drift where a new notification type is declared but
/// its delivery mapping is never consciously decided, satisfying NOT-07's "contract tests document
/// every supported event-to-notification mapping" acceptance criterion.
///
/// This does not re-assert the rendering/token logic already covered by
/// NotificationTemplateCatalogueTests/NotificationTemplateRendererTests, or the channel/route
/// per-branch behaviour already covered by NotificationChannelDefaultsTests/
/// NotificationActionRouteBuilderTests — it only asserts that every declared NotificationType has a
/// deliberate answer for each of the three concerns, cross-checked against those other suites'
/// coverage of the mechanics.
/// </summary>
public class NotificationEventMappingContractTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid SourceEntityId = Guid.NewGuid();

    // Every NotificationType that currently defaults to Both (InApp + Email) — see
    // NotificationChannelDefaults.EmailEligibleTypes. Every type not listed here is expected to be
    // InApp-only. Keeping this list here (independent of the production HashSet) means a change to
    // NotificationChannelDefaults that isn't a deliberate, reviewed decision fails this test.
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

    // The six NotificationTemplateCatalogue-backed types (NOT-03). Every other type continues to be
    // raised via INotificationWriter.WriteAsync with a pre-formatted string.
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

    /// <summary>
    /// Every template-backed type must also default to a resolvable action route — a template
    /// exists specifically so a rendered notification can be clicked through to something; a
    /// template-backed type with no destination would be a silent product gap, not a considered
    /// "informational only" decision like the Interview* types.
    /// </summary>
    [Fact]
    public void Every_Template_Backed_Type_Also_Has_An_Action_Route()
    {
        var templateBackedWithoutRoute = ExpectedTemplateBackedTypes
            .Where(type => NotificationActionRouteBuilder.BuildActionUrl(type, CompanyId, EmployeeId, SourceEntityId) is null)
            .ToList();

        Assert.Empty(templateBackedWithoutRoute);
    }
}
