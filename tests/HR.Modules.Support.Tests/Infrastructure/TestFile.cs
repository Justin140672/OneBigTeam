using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Tests.Infrastructure;

internal static class TestFile
{
    // Magic-byte headers for the content types used across these tests, so the real
    // SupportAttachmentValidator's signature check (ticket 4, P1) accepts them by default.
    private static readonly Dictionary<string, byte[]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47],
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF, 0xE0],
        ["application/pdf"] = [0x25, 0x50, 0x44, 0x46],
    };

    public static IFormFile Create(
        string fileName = "screenshot.png", string contentType = "image/png", int size = 128,
        bool validSignature = true)
    {
        var content = new byte[size];
        Array.Fill(content, (byte)0x1);

        if (validSignature && Signatures.TryGetValue(contentType, out var signature))
            signature.CopyTo(content, 0);

        return new FormFile(new MemoryStream(content), 0, content.Length, "Files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    public static IFormFileCollection Collection(params IFormFile[] files)
    {
        var collection = new FormFileCollection();
        collection.AddRange(files);
        return collection;
    }
}
