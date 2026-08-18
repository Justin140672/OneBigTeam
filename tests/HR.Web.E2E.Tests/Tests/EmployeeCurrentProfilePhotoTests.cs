using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Employee Edit page's profile photo header (EmployeeProfilePhotoHeader,
/// HR-only, rendered near the top of the page for users with CanManageEmployees):
/// - An employee with no current photo shows the initials placeholder, not an &lt;img&gt;.
/// - HR uploading a photo directly (via "Upload / Replace Photo") writes straight to the
///   current/approved photo — no pending-review step — and the header immediately reflects it.
/// - A photo submitted through the self-service flow (MyProfilePhotoHeader on MyProfile.razor)
///   sits pending until HR approves it from this header, after which the header shows the
///   newly-approved photo.
///
/// Uses seeded Acme employees (see EmployeesModule.cs seed data):
///   - Emma Jones   (30000000-0000-0000-0000-000000000009) — untouched by any other photo test,
///     used only to assert the "no photo yet" initial state.
///   - Priya Sharma (30000000-0000-0000-0000-000000000003) — used for the HR-direct-upload test.
///   - Carlos Rivera (30000000-0000-0000-0000-000000000010) — self-uploads a pending photo, then
///     HR (Laura) approves it.
/// </summary>
public sealed class EmployeeCurrentProfilePhotoTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid EmmaId  = Guid.Parse("30000000-0000-0000-0000-000000000009");
    private static readonly Guid PriyaId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

    [Fact]
    public async Task EmployeeWithNoCurrentPhoto_ShowsInitialsPlaceholder()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, EmmaId);

        Assert.True(await empEdit.HasProfilePhotoInitialsAsync(),
            "Expected the initials placeholder to be shown for an employee with no current profile photo");
        Assert.False(await empEdit.HasProfilePhotoImageAsync(),
            "Did not expect an <img> profile photo to be shown for an employee with no current photo");
    }

    [Fact]
    public async Task HrUploadsPhotoDirectly_HeaderShowsActualPhotoInsteadOfInitials()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, PriyaId);

        Assert.True(await empEdit.HasProfilePhotoInitialsAsync(),
            "Expected the initials placeholder before any photo has been uploaded for this employee");

        var tempFile = Path.Combine(Path.GetTempPath(), $"profile-photo-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPng());

            await empEdit.UploadProfilePhotoDirectAsync(tempFile);

            Assert.True(await empEdit.HasProfilePhotoImageAsync(),
                "Expected the header to show an actual photo (<img>) after HR uploaded one directly");
            Assert.False(await empEdit.HasProfilePhotoInitialsAsync(),
                "Did not expect the initials placeholder to still be shown after a direct HR upload");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HrApprovesPendingPhoto_HeaderShowsApprovedPhoto()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var myProfile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var tempFile = Path.Combine(Path.GetTempPath(), $"profile-photo-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPng());

            // ── Step 1: Carlos submits a photo via self-service — this always goes pending. ──
            await login.GoToAsync();
            await login.LoginAsync(CarlosEmail);

            await myProfile.GoToAsync(AcmeId, CarlosId);
            await myProfile.UploadMyProfilePhotoAsync(tempFile);

            Assert.True(await myProfile.HasPendingProfilePhotoBannerAsync(),
                "Expected a 'Pending approval' banner after Carlos's self-service upload");

            // ── Step 2: HR (Laura) reviews and approves it from the Employee Edit page. ──
            await login.SwitchAccountAsync(LauraEmail);

            await empEdit.GoToAsync(AcmeId, CarlosId);

            Assert.True(await empEdit.HasPendingProfilePhotoCardAsync(),
                "Expected the HR review card ('Pending Review') to be visible for Carlos's submitted photo");

            await empEdit.ApprovePendingProfilePhotoAsync();

            Assert.False(await empEdit.HasPendingProfilePhotoCardAsync(),
                "Expected the pending review card to disappear once approved");
            Assert.True(await empEdit.HasProfilePhotoImageAsync(),
                "Expected the header to show the newly-approved photo after HR approved it");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Builds a minimal-but-valid PNG (signature + IHDR chunk carrying width/height) at the
    /// server's minimum allowed dimensions (100x100 — see ImageUploadOptions.MinWidthPx /
    /// MinHeightPx). It has no IDAT pixel data, so it isn't a fully decodable image, but that's
    /// fine here: ProfilePhotoAvatar renders the &lt;img&gt; with an explicit inline width/height
    /// style regardless of decode success, and these tests only assert on which element
    /// (&lt;img&gt; vs. the initials &lt;span&gt;) is shown — never on the pixel content.
    /// </summary>
    private static byte[] BuildTestPng(int width = 200, int height = 200)
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG signature
        bytes.AddRange(BigEndianUInt32(13)); // IHDR chunk data length
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange(new byte[] { 0x08, 0x06, 0x00, 0x00, 0x00 }); // bit depth, color type, compression, filter, interlace
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // dummy CRC (not validated server-side)
        return [.. bytes];
    }

    private static byte[] BigEndianUInt32(int value) =>
    [
        (byte)((value >> 24) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF),
    ];
}
