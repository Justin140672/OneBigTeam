using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CreateTask;

namespace HR.Modules.Tasks.Tests;

public class CreateTaskValidatorTests
{
    private static CreateTaskRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Title = "Onboard new employee",
        Priority = TaskPriority.Medium,
        Source = TaskSource.Manual
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new CreateTaskValidator().Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_All_Optional_Fields()
    {
        var request = ValidRequest() with
        {
            Description = "Full details here",
            DueDate = new DateOnly(2026, 12, 31),
            AssignedEmployeeId = Guid.NewGuid(),
            AssignedUserId = Guid.NewGuid()
        };

        Assert.True(new CreateTaskValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var request = ValidRequest() with { Title = string.Empty };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_200_Characters()
    {
        var request = ValidRequest() with { Title = new string('A', 201) };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Title));
    }

    [Fact]
    public void Validate_Passes_When_Title_Is_Exactly_200_Characters()
    {
        var request = ValidRequest() with { Title = new string('A', 200) };

        Assert.True(new CreateTaskValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_2000_Characters()
    {
        var request = ValidRequest() with { Description = new string('A', 2001) };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Description));
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Null()
    {
        var request = ValidRequest() with { Description = null };

        Assert.True(new CreateTaskValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Priority_Is_Out_Of_Range()
    {
        var request = ValidRequest() with { Priority = (TaskPriority)99 };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Priority));
    }

    [Fact]
    public void Validate_Fails_When_Source_Is_Out_Of_Range()
    {
        var request = ValidRequest() with { Source = (TaskSource)99 };

        var result = new CreateTaskValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTaskRequest.Source));
    }
}
