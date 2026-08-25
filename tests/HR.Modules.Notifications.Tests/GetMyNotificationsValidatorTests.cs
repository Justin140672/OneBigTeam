using HR.Modules.Notifications.Features.GetMyNotifications;

namespace HR.Modules.Notifications.Tests;

public class GetMyNotificationsValidatorTests
{
    private static readonly GetMyNotificationsValidator Validator = new();

    private static GetMyNotificationsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        PageNumber = 1,
        PageSize = 50,
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    // ── CompanyId ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.Empty,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.CompanyId));
    }

    // ── PageNumber ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PageNumber_Zero_Fails()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 0,
            PageSize = 50,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.PageNumber));
    }

    [Fact]
    public void Validate_PageNumber_Negative_Fails()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = -1,
            PageSize = 50,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.PageNumber));
    }

    [Fact]
    public void Validate_PageNumber_One_Passes()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
        });
        Assert.True(result.IsValid);
    }

    // ── PageSize ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PageSize_Zero_Fails()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 0,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.PageSize));
    }

    [Fact]
    public void Validate_PageSize_One_Passes()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 1,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PageSize_OneHundred_Passes()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 100,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PageSize_OneHundredAndOne_Fails()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 101,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.PageSize));
    }

    // ── CreatedFrom / CreatedTo ────────────────────────────────────────────

    [Fact]
    public void Validate_CreatedTo_Before_CreatedFrom_Fails()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(-1);
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            CreatedFrom = from,
            CreatedTo = to,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMyNotificationsRequest.CreatedTo));
    }

    [Fact]
    public void Validate_CreatedTo_Equal_To_CreatedFrom_Passes()
    {
        var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            CreatedFrom = date,
            CreatedTo = date,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CreatedTo_After_CreatedFrom_Passes()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            CreatedFrom = from,
            CreatedTo = from.AddDays(1),
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CreatedFrom_Only_Passes()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            CreatedFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CreatedTo_Only_Passes()
    {
        var result = Validator.Validate(new GetMyNotificationsRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 50,
            CreatedTo = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        });
        Assert.True(result.IsValid);
    }
}
