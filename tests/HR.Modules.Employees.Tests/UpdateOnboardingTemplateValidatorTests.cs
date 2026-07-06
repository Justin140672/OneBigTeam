using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Features.UpdateOnboardingTemplate;

namespace HR.Modules.Employees.Tests;

public class UpdateOnboardingTemplateValidatorTests
{
    private readonly UpdateOnboardingTemplateValidator _validator = new();

    private static UpdateOnboardingTemplateRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Name = "Standard Onboarding",
        Tasks =
        [
            new UpdateOnboardingTemplateTaskItem(null, "Set up laptop", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0),
        ],
    };

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var request = ValidRequest() with { Id = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var request = ValidRequest() with { Name = "" };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Task_Title_Is_Empty()
    {
        var request = ValidRequest() with
        {
            Tasks = [new UpdateOnboardingTemplateTaskItem(null, "", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0)],
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Task_DueDaysAfterStart_Is_Negative()
    {
        var request = ValidRequest() with
        {
            Tasks = [new UpdateOnboardingTemplateTaskItem(null, "Task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, -1, 0)],
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Task_DisplayOrder_Is_Negative()
    {
        var request = ValidRequest() with
        {
            Tasks = [new UpdateOnboardingTemplateTaskItem(null, "Task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, -1)],
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }
}
