using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SupersedeEmployeeNote;

namespace HR.Modules.Employees.Tests;

public class SupersedeEmployeeNoteValidatorTests
{
    private static SupersedeEmployeeNoteRequest ValidRequest() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NoteCategory.General, "Updated note text.", false);

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_OriginalNoteId_Is_Empty()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { OriginalNoteId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.OriginalNoteId));
    }

    [Fact]
    public void Validate_Fails_When_Category_Is_Not_A_Defined_Value()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { Category = (NoteCategory)999 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.Category));
    }

    [Fact]
    public void Validate_Fails_When_NoteText_Is_Empty()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { NoteText = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.NoteText));
    }

    [Fact]
    public void Validate_Fails_When_NoteText_Is_Whitespace_Only()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { NoteText = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.NoteText));
    }

    [Fact]
    public void Validate_Fails_When_NoteText_Exceeds_MaxLength()
    {
        var v = new SupersedeEmployeeNoteValidator();
        var result = v.Validate(ValidRequest() with { NoteText = new string('a', 4001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SupersedeEmployeeNoteRequest.NoteText));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new SupersedeEmployeeNoteValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
