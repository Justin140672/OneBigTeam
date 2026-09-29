using System.Reflection;
using System.Text.RegularExpressions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;

namespace HR.Modules.Identity.Tests;

public class AccountDisablementLogIdentifierInvariantTests
{
    private static readonly string[] ExpectedStatuses =
    [
        AccountDisablement.StatusPending,
        AccountDisablement.StatusProcessing,
        AccountDisablement.StatusProcessed,
        AccountDisablement.StatusFailed,
    ];

    [Fact]
    public void Every_Public_ProcessAsync_Overload_Takes_Only_Guid_Parameters()
    {
        var overloads = typeof(AccountDisablementJob)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == nameof(AccountDisablementJob.ProcessAsync))
            .ToList();

        Assert.NotEmpty(overloads);
        foreach (var overload in overloads)
        {
            Assert.All(overload.GetParameters(), p =>
                Assert.True(p.ParameterType == typeof(Guid),
                    $"{overload} parameter '{p.Name}' is {p.ParameterType}, expected System.Guid."));
        }
    }

    [Theory]
    [InlineData(nameof(AccountDisablement.Id))]
    [InlineData(nameof(AccountDisablement.CompanyId))]
    [InlineData(nameof(AccountDisablement.ApplicationUserId))]
    [InlineData(nameof(AccountDisablement.EmployeeId))]
    public void Logged_Identifier_Properties_Are_Guids(string propertyName)
    {
        var property = typeof(AccountDisablement).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.PropertyType);
    }

    [Fact]
    public void Reconciliation_Reason_Status_Has_No_Public_Setter()
    {
        var status = typeof(AccountDisablement).GetProperty(nameof(AccountDisablement.Status), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(status);
        var setter = status!.GetSetMethod(nonPublic: true);
        Assert.True(setter is null || !setter.IsPublic, "AccountDisablement.Status must not be publicly settable.");
    }

    [Fact]
    public void Status_Constants_Are_A_Closed_Set_Of_Plain_Lowercase_Words()
    {
        var constants = typeof(AccountDisablement)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("Status", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ExpectedStatuses.OrderBy(v => v, StringComparer.Ordinal), constants);
        Assert.All(constants, v => Assert.Matches(new Regex(@"^[a-z]+\z"), v));
    }

    [Fact]
    public void Every_Lifecycle_Transition_Leaves_Status_In_The_Closed_Set()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var disablement = AccountDisablement.CreatePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        Assert.Contains(disablement.Status, ExpectedStatuses);

        disablement.Claim(Guid.NewGuid(), now);
        Assert.Contains(disablement.Status, ExpectedStatuses);

        disablement.MarkFailed("caller\r\ntext is a failure reason, never the Status", now, maxAutomaticAttempts: 5);
        Assert.Contains(disablement.Status, ExpectedStatuses);

        disablement.ResetForRetry();
        Assert.Contains(disablement.Status, ExpectedStatuses);

        disablement.MarkProcessing(now);
        Assert.Contains(disablement.Status, ExpectedStatuses);

        disablement.MarkProcessed(now);
        Assert.Contains(disablement.Status, ExpectedStatuses);
    }
}
