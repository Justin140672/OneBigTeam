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
}
