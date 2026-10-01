using HR.Web.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Tests;

public class UploadFileValidationTests
{
    private static IBrowserFile File(string name, long size) => new FakeBrowserFile(name, size);

    private sealed class FakeBrowserFile(string name, long size) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => size;
        public string ContentType => "application/octet-stream";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => Stream.Null;
    }

    [Fact]
    public void Accepts_Allowed_Extension_Within_Limit() =>
        Assert.Null(UploadFileValidation.ValidateDocument(File("cv.PDF", 1024)));

    [Fact]
    public void Rejects_Wrong_Type() =>
        Assert.Equal("The file must be a PDF, Word (DOC or DOCX), JPG or PNG file.", UploadFileValidation.ValidateDocument(File("a.exe", 10)));

    [Fact]
    public void Rejects_Empty_File() =>
        Assert.Equal("The selected file is empty.", UploadFileValidation.ValidateDocument(File("a.pdf", 0)));

    [Fact]
    public void Rejects_Oversized_File() =>
        Assert.Equal("The file is larger than the 20 MB limit.", UploadFileValidation.ValidateDocument(File("a.pdf", 21 * 1024 * 1024)));

    [Fact]
    public void Support_Attachments_Use_Ten_Mb_Limit() =>
        Assert.Equal("The file is larger than the 10 MB limit.", UploadFileValidation.ValidateSupportAttachment(File("a.png", 11 * 1024 * 1024)));
}
