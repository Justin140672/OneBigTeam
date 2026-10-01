namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal static class PdfBytes
{
    private static readonly byte[] Header = "%PDF-"u8.ToArray();

    public static byte[] Create(int size)
    {
        var bytes = new byte[size];
        Header.AsSpan(0, Math.Min(Header.Length, size)).CopyTo(bytes);
        return bytes;
    }
}
