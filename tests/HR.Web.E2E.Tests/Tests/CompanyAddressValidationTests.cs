using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers NestedDataAnnotationsValidator's field-level revalidation (Components/Controls/
/// NestedDataAnnotationsValidator.cs) via the Company Profile tab's address fields — the one place
/// in the app that validator is actually wired up. Before this fix, the validator only revalidated
/// on submit (OnValidationRequested), never on individual field changes (OnFieldChanged), so a
/// corrected field's error message stayed on screen until the next full Save.
/// </summary>
public sealed class CompanyAddressValidationTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task CorrectingAnAddressField_ClearsItsValidationMessage_WithoutRequiringAnotherSave()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalLine1 = await companyEdit.GetFirstAddressLine1Async();

        try
        {
            // Clear Line 1, save to trigger the validation failure and surface the message.
            await companyEdit.SetFirstAddressLine1Async("");
            await companyEdit.SaveAsync();
            Assert.True(await companyEdit.IsAddressLine1ValidationMessageVisibleAsync(),
                "Expected the 'Line 1 is required.' message after saving with it blank");

            // Fix the field — the message must disappear immediately, without clicking Save again.
            await companyEdit.SetFirstAddressLine1Async("1 Example Street");

            Assert.False(await companyEdit.IsAddressLine1ValidationMessageVisibleAsync(),
                "Expected the 'Line 1 is required.' message to clear as soon as the field was corrected");
        }
        finally
        {
            // Restore the seeded value so this test doesn't leak state into other tests that
            // share Acme's company record.
            await companyEdit.SetFirstAddressLine1Async(originalLine1);
            await companyEdit.SaveAsync();
        }
    }
}
