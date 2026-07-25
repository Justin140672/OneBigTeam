using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Notes tab on the employee edit page (HR-only): adding notes, important-note
/// pinning/grouping, and the supersede workflow (original note stays visible with a "Superseded"
/// indicator alongside the new replacement note).
///
/// Uses "Tom Williams" (ID: 30000000-0000-0000-0000-000000000004), who has no seeded notes, so
/// notes created within each test are the only ones present — avoiding interference between
/// tests and from seed data (seed data is inserted directly into the database and does not go
/// through the audited handlers).
/// </summary>
[Collection("E2E")]
public sealed class EmployeeNotesTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task HrAdmin_CanAddNote_And_SeeItInTheList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);

        Assert.True(
            await empEdit.HasNotesTabAsync(),
            "Expected a 'Notes' tab on the employee edit page for an HR administrator");

        await empEdit.OpenNotesTabAsync();

        var noteText = $"Discussed onboarding progress {Guid.NewGuid():N}";

        await empEdit.ClickAddNoteAsync();
        await empEdit.SelectAddNoteCategoryAsync("Performance");
        await empEdit.FillAddNoteTextAsync(noteText);
        await empEdit.SubmitAddNoteDialogAsync();

        Assert.True(
            await empEdit.NoteCard(noteText).First.IsVisibleAsync(),
            "Expected the newly added note to appear in the Notes tab list");
    }

    [Fact]
    public async Task ImportantNotes_ArePinned_AboveStandardNotes()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenNotesTabAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var standardText = $"Standard note {suffix}";
        var importantText = $"Important note {suffix}";

        // Add the standard note first, then the important one — if ordering were purely
        // insertion order, the standard note would come first. Asserting the important note
        // still renders above it proves the "pinned" grouping behaviour.
        await empEdit.ClickAddNoteAsync();
        await empEdit.SelectAddNoteCategoryAsync("General");
        await empEdit.FillAddNoteTextAsync(standardText);
        await empEdit.SubmitAddNoteDialogAsync();

        await empEdit.ClickAddNoteAsync();
        await empEdit.SelectAddNoteCategoryAsync("General");
        await empEdit.FillAddNoteTextAsync(importantText);
        await empEdit.CheckAddNoteImportantAsync();
        await empEdit.SubmitAddNoteDialogAsync();

        Assert.True(
            await empEdit.NoteCardHasImportantBadgeAsync(importantText),
            "Expected the important note to show the 'Important' badge");

        var importantY = await empEdit.GetNoteCardYPositionAsync(importantText);
        var standardY = await empEdit.GetNoteCardYPositionAsync(standardText);

        Assert.NotNull(importantY);
        Assert.NotNull(standardY);
        Assert.True(
            importantY < standardY,
            $"Expected the Important note (y={importantY}) to render above the standard note (y={standardY})");
    }

    [Fact]
    public async Task SupersedingNote_KeepsOriginalVisible_WithSupersededIndicator_AndShowsReplacement()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenNotesTabAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var originalText = $"Original note {suffix}";
        var replacementText = $"Replacement note {suffix}";

        await empEdit.ClickAddNoteAsync();
        await empEdit.SelectAddNoteCategoryAsync("General");
        await empEdit.FillAddNoteTextAsync(originalText);
        await empEdit.SubmitAddNoteDialogAsync();

        await empEdit.ClickSupersedeNoteAsync(originalText);
        await empEdit.FillSupersedeNoteTextAsync(replacementText);
        await empEdit.SubmitSupersedeNoteDialogAsync();

        Assert.True(
            await empEdit.NoteCard(originalText).First.IsVisibleAsync(),
            "Expected the original note's text to remain visible after being superseded");
        Assert.True(
            await empEdit.NoteCardHasSupersededBadgeAsync(originalText),
            "Expected the original note to show a 'Superseded' badge");
        Assert.True(
            await empEdit.NoteCard(replacementText).First.IsVisibleAsync(),
            "Expected the replacement note's text to be visible");
    }

    // Note: a "non-HR personas can't reach the Notes tab" test was deliberately not added here.
    // The Notes tab is wrapped in @if(Session.IsHrAdministrator) inside EmployeeEdit.razor, but
    // that's defense in depth on top of a page-level guard: any user without
    // Session.CanManageEmployees is redirected off the whole employee edit page before any tab
    // (including Notes) ever renders. UnauthorizedAccessTests.Employee_CannotAccess_
    // AnotherEmployeesAdminProfile and Manager_CannotAccess_AnotherEmployeesAdminProfile already
    // cover that exact redirect for both roles on this exact URL — a Notes-specific duplicate of
    // the same assertion would add no new signal, since the Notes tab is never actually reached
    // in either scenario.
}
