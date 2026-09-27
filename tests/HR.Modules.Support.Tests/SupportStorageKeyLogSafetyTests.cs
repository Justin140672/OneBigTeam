using System.Text.RegularExpressions;
using HR.Modules.Support.Services;

namespace HR.Modules.Support.Tests;

/// <summary>
/// CodeQL #63-#65 (log forging via storage keys): <see cref="UploadedAttachmentCleanupScope"/>
/// logs orphaned-attachment cleanup failures with <see cref="UploadedAttachmentCleanupScope.RedactStorageKey"/>.
/// Real keys are "support/{companyId}/{requestId}/{server GUID}{allow-listed extension}", so the
/// retained tail is already safe; these tests prove that (a) the redaction keeps only a short,
/// character-allow-listed tail for every real key shape, (b) even a hypothetical hostile key can
/// never emit a control character, and (c) the extension allow-list rejects any file name whose
/// extension carries control or encoded characters, so nothing hostile reaches a key in the first place.
/// </summary>
public class SupportStorageKeyLogSafetyTests
{
    private const char LineSeparator = (char)0x2028;

    private static readonly Regex RealKeyRedaction = new(@"^\*\*\*[A-Za-z0-9._-]{1,12}\z");
    private static readonly Regex HostileKeyRedaction = new(@"^\*\*\*[A-Za-z0-9._?-]{1,12}\z");

    private readonly SupportAttachmentValidator _validator = new();

    public static TheoryData<string> AllowedExtensions()
    {
        var data = new TheoryData<string>();
        foreach (var ext in SupportAttachmentPolicy.AllowedExtensions)
            data.Add(ext);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedExtensions))]
    public void RedactStorageKey_Keeps_Only_Last_12_Chars_Of_Final_Segment_For_Real_Keys(string extension)
    {
        var finalSegment = $"{Guid.NewGuid():N}{extension}";
        var key = $"support/{Guid.NewGuid()}/{Guid.NewGuid()}/{finalSegment}";

        var redacted = UploadedAttachmentCleanupScope.RedactStorageKey(key);

        Assert.Equal("***" + finalSegment[^12..], redacted);
        Assert.Matches(RealKeyRedaction, redacted);
        Assert.DoesNotContain("support/", redacted);
    }

    public static TheoryData<string> HostileKeys => new()
    {
        "support/a/b/x.pdf\r\nFORGED",
        "support/a/b/x\n.pdf",
        "x.pdf\u0000",
        "x" + LineSeparator + ".pdf",
        "evil\r\n",
    };

    [Theory]
    [MemberData(nameof(HostileKeys))]
    public void RedactStorageKey_Never_Emits_Control_Or_Line_Separator_Chars_For_Hostile_Keys(string hostileKey)
    {
        var redacted = UploadedAttachmentCleanupScope.RedactStorageKey(hostileKey);

        Assert.DoesNotContain(redacted, char.IsControl);
        Assert.DoesNotContain(LineSeparator, redacted);
        Assert.Matches(HostileKeyRedaction, redacted);
    }

    [Theory]
    [InlineData("evil.pdf\r\nX")]
    [InlineData("evil.pdf\n")]
    [InlineData("evil.pd\nf")]
    [InlineData("evil.pdf\u0000")]
    [InlineData("evil.pdf%0D%0A")]
    [InlineData("evil.exe")]
    public void ValidateFile_Rejects_Names_Whose_Extension_Is_Not_Allow_Listed(string fileName)
    {
        var result = _validator.ValidateFile(fileName, "application/pdf", 100);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateFile_Accepts_Allow_Listed_Extension_Case_Insensitively()
    {
        var result = _validator.ValidateFile("report.PDF", "application/pdf", 100);

        Assert.True(result.IsSuccess);
    }
}
