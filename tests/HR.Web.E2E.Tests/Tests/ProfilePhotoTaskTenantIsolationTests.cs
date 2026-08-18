using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a profile-photo review task created for one company's employee (via
/// self-service upload — see UploadMyProfilePhotoHandler) is never visible in another
/// company's HR Inbox, even though HR Inbox tasks are unassigned (assignedEmployeeId: null)
/// and therefore not filtered by employee ownership the way a "My Tasks" widget is — the only
/// thing keeping them apart is server-side CompanyId scoping. Mirrors the cross-tenant task
/// check already covered for Leave requests in TenantIsolationTests.cs, applied to the
/// employee profile-photo review workflow (Documents module).
///
/// There is no seeded Beta Corp HR Administrator (only Alice/Manager and Bob/Employee — see
/// IdentityModule.cs), so this test only asserts the negative (Acme must not see Beta Corp's
/// task), not the positive (Beta Corp HR does see it) — the negative assertion is the actual
/// security property in question.
/// </summary>
public sealed class ProfilePhotoTaskTenantIsolationTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    // ── Company 1 — Acme Corporation ─────────────────────────────────────────
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example"; // Acme HR Administrator

    // ── Company 2 — Beta Corp ────────────────────────────────────────────────
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid BobId      = Guid.Parse("30000000-0000-0000-0000-000000000012");
    private const string BobEmail = "bob.taylor@betacorp.example";

    [Fact]
    public async Task BetaCorpProfilePhotoReviewTask_IsInvisibleToAcmeHrInbox()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var myProfile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var hrInbox   = new HrInboxPage(_page, _fixture.WebBaseUrl);

        var tempFile = Path.Combine(Path.GetTempPath(), $"tenant-isolation-photo-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPng());

            // ── Step 1: Bob (Beta Corp employee) submits a profile photo for review. ──
            // This creates a PendingProfilePhoto and an unassigned HR-inbox task
            // (TaskSource.Document / TaskActionType.Review) scoped to BetaCorpId.
            await login.GoToAsync();
            await login.LoginAsync(BobEmail);

            await myProfile.GoToAsync(BetaCorpId, BobId);
            await myProfile.UploadMyProfilePhotoAsync(tempFile);

            Assert.True(await myProfile.HasPendingProfilePhotoBannerAsync(),
                "Expected a 'Pending approval' banner after Bob's self-service upload — " +
                "if this fails, the review task was never created and the isolation check below is meaningless.");

            // ── Step 2: Acme's HR Inbox must not contain Bob's review task. ──
            await login.SwitchAccountAsync(LauraEmail);

            await hrInbox.GoToAsync(AcmeId);
            var acmeInboxTitles = await hrInbox.GetTaskTitlesAsync();

            Assert.DoesNotContain(acmeInboxTitles,
                t => t.Contains(BobId.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(acmeInboxTitles,
                t => t.Contains("Bob Taylor", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Builds a minimal-but-valid PNG (signature + IHDR chunk carrying width/height) at the
    /// dimensions ImageUploadValidator requires — mirrors the identical helper already used in
    /// EmployeeCurrentProfilePhotoTests.cs and the UploadEmployeeProfilePhoto integration tests.
    /// </summary>
    private static byte[] BuildTestPng(int width = 400, int height = 300)
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // signature
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D }); // IHDR chunk data length
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange(new byte[] { 0x08, 0x06, 0x00, 0x00, 0x00 }); // bit depth, color type, compression, filter, interlace
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // dummy CRC (not validated)
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
