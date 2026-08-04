using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Tests.Infrastructure;

internal static class TestFile
{
    public static IFormFile Create(string fileName = "screenshot.png", string contentType = "image/png", int size = 128)
    {
        var content = new byte[size];
        Array.Fill(content, (byte)0x1);
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
