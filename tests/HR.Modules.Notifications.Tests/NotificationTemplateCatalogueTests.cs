using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

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
