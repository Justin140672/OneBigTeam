namespace HR.Modules.Documents.Tests.Infrastructure;

internal static class ImageTestBytes
{
    public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0];

    public static byte[] BuildPng(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange(BigEndianUInt32(13));
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange([0x08, 0x06, 0x00, 0x00, 0x00]);
        bytes.AddRange([0x00, 0x00, 0x00, 0x00]);
        return [.. bytes];
    }

    public static byte[] BuildTruncatedPng()
    {
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange([0x00, 0x00, 0x00, 0x0D]);
        return [.. bytes];
    }

    public static byte[] BuildJpeg(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(JpegSignature);
        bytes.AddRange([0x00, 0x10]);
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange([0x01, 0x01]);
        bytes.Add(0x00);
        bytes.AddRange([0x00, 0x01]);
        bytes.AddRange([0x00, 0x01]);
        bytes.Add(0x00);
        bytes.Add(0x00);

        bytes.AddRange([0xFF, 0xC0]);
        bytes.AddRange([0x00, 0x11]);
        bytes.Add(0x08);
        bytes.AddRange(BigEndianUInt16(height));
        bytes.AddRange(BigEndianUInt16(width));
        bytes.Add(0x03);
        bytes.AddRange([0x01, 0x11, 0x00]);
        bytes.AddRange([0x02, 0x11, 0x01]);
        bytes.AddRange([0x03, 0x11, 0x01]);

        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    public static byte[] BuildTruncatedJpeg()
    {
        var bytes = new List<byte>();
        bytes.AddRange(JpegSignature);
        bytes.AddRange([0x00, 0x10]);
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange([0x01, 0x01]);
        bytes.Add(0x00);
        bytes.AddRange([0x00, 0x01]);
        bytes.AddRange([0x00, 0x01]);
        bytes.Add(0x00);
        bytes.Add(0x00);
        return [.. bytes];
    }

    private static byte[] BigEndianUInt32(int value) =>
    [
        (byte)((value >> 24) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF),
    ];

    private static byte[] BigEndianUInt16(int value) =>
    [
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF),
    ];
}
