using HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

namespace HR.Modules.Companies.Tests;

public class UpdateDocumentReminderSettingsValidatorTests
{
    private static UpdateDocumentReminderSettingsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        RemindersEnabled = true,
        OffsetDays1 = 90,
        OffsetDays2 = 30,
        OffsetDays3 = 7,
        Version = 1,
    };

    [Fact]
    public void Validate_Passes_For_Default_90_30_7_Schedule()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        Assert.True(validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Fully_Valid_Custom_Schedule()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 60, OffsetDays2 = 21, OffsetDays3 = 3 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentReminderSettingsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Zero()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentReminderSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Negative()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateDocumentReminderSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Passes_When_Version_Is_One()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        Assert.True(validator.Validate(ValidRequest() with { Version = 1 }).IsValid);
    }

    // ── Positive-value checks, each slot independently ──────────────────────────

    [Fact]
    public void Validate_Fails_When_OffsetDays1_Is_Zero()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 0 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays1_Is_Negative()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = -5 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_Is_Zero()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 90, OffsetDays2 = 0, OffsetDays3 = null });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays3_Is_Zero()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays3 = 0 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays3_Is_Negative()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays3 = -1 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_OffsetDays_Value_Is_One()
    {
        // Boundary: 1 is the smallest allowed positive value.
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 3, OffsetDays2 = 2, OffsetDays3 = 1 });
        Assert.True(result.IsValid);
    }

    // ── At least one configured while enabled ───────────────────────────────────

    [Fact]
    public void Validate_Fails_When_Enabled_And_All_Offsets_Are_Null()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with
        {
            RemindersEnabled = true,
            OffsetDays1 = null,
            OffsetDays2 = null,
            OffsetDays3 = null,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Disabled_And_All_Offsets_Are_Null()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with
        {
            RemindersEnabled = false,
            OffsetDays1 = null,
            OffsetDays2 = null,
            OffsetDays3 = null,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Enabled_And_Only_One_Offset_Is_Configured()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with
        {
            RemindersEnabled = true,
            OffsetDays1 = 60,
            OffsetDays2 = null,
            OffsetDays3 = null,
        });

        Assert.True(result.IsValid);
    }

    // ── Uniqueness ───────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Fails_When_OffsetDays1_And_OffsetDays2_Are_Equal()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 30, OffsetDays2 = 30, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_And_OffsetDays3_Are_Equal()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 90, OffsetDays2 = 7, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays1_And_OffsetDays3_Are_Equal()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 7, OffsetDays2 = null, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    // ── Strict-decreasing ordering, including "skip slot 2" ─────────────────────

    [Fact]
    public void Validate_Fails_When_OffsetDays1_Is_Less_Than_OffsetDays2()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 20, OffsetDays2 = 30, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays1_Equals_OffsetDays2()
    {
        // Equality is covered by the uniqueness rule too, but this pins down that the ordering
        // rule itself is a strict (not "greater-than-or-equal") comparison.
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 30, OffsetDays2 = 30, OffsetDays3 = null });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_Is_Less_Than_OffsetDays3()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 90, OffsetDays2 = 5, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_Equals_OffsetDays3()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 90, OffsetDays2 = 7, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_OffsetDays2_Is_Null_And_OffsetDays1_Is_Greater_Than_OffsetDays3()
    {
        // Valid "skip slot 2" case explicitly called out in the spec: 90 / null / 7.
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 90, OffsetDays2 = null, OffsetDays3 = 7 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_Is_Null_And_OffsetDays1_Is_Not_Greater_Than_OffsetDays3()
    {
        // Invalid "skip slot 2" case explicitly called out in the spec: 5 / null / 7.
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 5, OffsetDays2 = null, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OffsetDays2_Is_Null_And_OffsetDays1_Equals_OffsetDays3()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 7, OffsetDays2 = null, OffsetDays3 = 7 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_OffsetDays1_Is_Configured()
    {
        // No ordering constraint can be violated with a single configured slot.
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = 45, OffsetDays2 = null, OffsetDays3 = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_OffsetDays3_Is_Configured()
    {
        var validator = new UpdateDocumentReminderSettingsValidator();
        var result = validator.Validate(ValidRequest() with { OffsetDays1 = null, OffsetDays2 = null, OffsetDays3 = 5 });
        Assert.True(result.IsValid);
    }
}
