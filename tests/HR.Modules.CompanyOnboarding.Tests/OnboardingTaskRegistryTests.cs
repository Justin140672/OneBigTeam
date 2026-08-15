using HR.Modules.CompanyOnboarding.Services;
using HR.Modules.CompanyOnboarding.Tests.Infrastructure;

namespace HR.Modules.CompanyOnboarding.Tests;

public class OnboardingTaskRegistryTests
{
    [Fact]
    public void Tasks_Are_Ordered_By_Order_Ascending_Regardless_Of_Registration_Order()
    {
        var third = new FakeOnboardingTaskDefinition("third", order: 3, isCompleted: false);
        var first = new FakeOnboardingTaskDefinition("first", order: 1, isCompleted: false);
        var second = new FakeOnboardingTaskDefinition("second", order: 2, isCompleted: false);

        var registry = new OnboardingTaskRegistry([third, first, second]);

        Assert.Equal(["first", "second", "third"], registry.Tasks.Select(t => t.Key).ToArray());
    }

    [Fact]
    public void Tasks_Is_Empty_When_No_Definitions_Are_Registered()
    {
        var registry = new OnboardingTaskRegistry([]);

        Assert.Empty(registry.Tasks);
    }

    [Fact]
    public void Tasks_With_Duplicate_Order_Preserves_Original_Registration_Order()
    {
        // OrderBy is a stable sort, so equal keys must retain their input order.
        var first = new FakeOnboardingTaskDefinition("first", order: 1, isCompleted: false);
        var second = new FakeOnboardingTaskDefinition("second", order: 1, isCompleted: false);

        var registry = new OnboardingTaskRegistry([first, second]);

        Assert.Equal(["first", "second"], registry.Tasks.Select(t => t.Key).ToArray());
    }
}
