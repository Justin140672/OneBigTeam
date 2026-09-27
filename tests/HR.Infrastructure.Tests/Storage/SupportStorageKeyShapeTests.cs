using System.Text.RegularExpressions;
using HR.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace HR.Infrastructure.Tests.Storage;

/// <summary>
/// CodeQL #63-#65 (log forging via storage keys): support-attachment storage keys are later logged
/// (redacted) by the upload cleanup paths. This pins the key shape produced by
/// <see cref="LocalSupportAttachmentStorageService"/>: "support/{companyId}/{requestId}/" + a
/// server-generated 32-hex GUID + the (allow-listed) extension — the caller-supplied file name
/// never reaches the key, so it cannot smuggle control characters into a log line.
/// </summary>
public sealed class SupportStorageKeyShapeTests
{
    [Fact]
    public async Task UploadAsync_Key_Is_Folder_Plus_Server_Guid_Plus_Extension_Only()
    {
        var sut = new LocalSupportAttachmentStorageService(new HttpContextAccessor(), new HR.Infrastructure.Abstractions.LocalStorageUrlSigner(TimeProvider.System));
        var companyId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        using var upload = new MemoryStream([0x25, 0x50, 0x44, 0x46]);

        var storageKey = await sut.UploadAsync(
            upload, "cv.pdf", "application/pdf", $"support/{companyId}/{requestId}", CancellationToken.None);

        try
        {
            var expected = new Regex(
                $@"^support/{Regex.Escape(companyId.ToString())}/{Regex.Escape(requestId.ToString())}/[0-9a-f]{{32}}\.pdf\z");
            Assert.Matches(expected, storageKey);
            Assert.DoesNotContain("cv.pdf", storageKey);
            Assert.DoesNotContain("/cv", storageKey);
        }
        finally
        {
            await sut.DeleteAsync(storageKey, CancellationToken.None);
        }
    }
}
