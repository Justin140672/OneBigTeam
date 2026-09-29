using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

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
            await companyEdit.SetFirstAddressLine1Async("");
            await companyEdit.SaveAsync();
            Assert.True(await companyEdit.IsAddressLine1ValidationMessageVisibleAsync(),
                "Expected the 'Line 1 is required.' message after saving with it blank");

            await companyEdit.SetFirstAddressLine1Async("1 Example Street");

            Assert.False(await companyEdit.IsAddressLine1ValidationMessageVisibleAsync(),
                "Expected the 'Line 1 is required.' message to clear as soon as the field was corrected");
        }
        finally
        {
            await companyEdit.SetFirstAddressLine1Async(originalLine1);
            await companyEdit.SaveAsync();
        }
    }
}
