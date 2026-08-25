using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

// NOT-03: catalogue-wide consistency checks — not per-render behaviour (see
// NotificationTemplateRendererTests for that). These guard against a missing/extra registered type
// and a typo'd "{Token}" placeholder that doesn't match a declared RequiredTokens/OptionalTokens entry.
public class NotificationTemplateCatalogueTests
{
    private static readonly NotificationType[] ExpectedTypes =
    [
        NotificationType.LeaveRequested,
        NotificationType.LeaveApproved,
        NotificationType.EmployeeCreated,
        NotificationType.CandidateHired,
        NotificationType.DocumentExpiring,
        NotificationType.TaskAssigned,
    ];

    [Fact]
    public void All_Contains_Exactly_The_Six_Required_NotificationTypes()
    {
        var actual = NotificationTemplateCatalogue.All.Keys.ToHashSet();
        var expected = ExpectedTypes.ToHashSet();

        Assert.Equal(expected, actual);
    }

    // NotificationTemplate itself is internal to HR.Modules.Notifications, so [MemberData] can only
    // hand the (public) test methods the NotificationType key — the template is looked up inside
    // each method via NotificationTemplateCatalogue.TryGet, keeping this test class public (required
    // by xUnit1000) without an accessibility mismatch on the method signature.
    public static IEnumerable<object[]> AllTemplateTypes() =>
        NotificationTemplateCatalogue.All.Keys.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(AllTemplateTypes))]
    public void FindUndeclaredTokenPlaceholders_Returns_Empty_For_Every_Catalogue_Template(NotificationType type)
    {
        NotificationTemplateCatalogue.TryGet(type, out var template);
        var undeclared = NotificationTemplateRenderer.FindUndeclaredTokenPlaceholders(template!);

        Assert.Empty(undeclared);
    }

    [Theory]
    [MemberData(nameof(AllTemplateTypes))]
    public void Every_Catalogue_Template_Has_A_Version_Of_At_Least_One(NotificationType type)
    {
        NotificationTemplateCatalogue.TryGet(type, out var template);
        Assert.True(template!.Version >= 1);
    }
}
