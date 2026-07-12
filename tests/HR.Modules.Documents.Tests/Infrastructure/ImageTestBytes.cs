namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Hand-rolled minimal PNG/JPEG byte sequences for exercising <c>ImageUploadValidator</c>
/// (and anything that depends on it) without pulling in an actual image library.
/// </summary>
internal static class ImageTestBytes
{
    // 8-byte PNG signature, matches the magic bytes ImageUploadValidator checks for "image/png".
    public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // 4-byte JFIF SOI+APP0 prefix, matches one of the magic byte sequences ImageUploadValidator
    // checks for "image/jpeg".
    public static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0];

    /// <summary>
    /// Builds a minimal-but-valid PNG byte stream: signature + IHDR chunk carrying the given
    /// width/height at the big-endian offsets (16/20) that ImageUploadValidator reads.
    /// </summary>
    public static byte[] BuildPng(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange(BigEndianUInt32(13)); // IHDR chunk data length
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange([0x08, 0x06, 0x00, 0x00, 0x00]); // bit depth, color type, compression, filter, interlace
        bytes.AddRange([0x00, 0x00, 0x00, 0x00]); // dummy CRC (not validated)
        return [.. bytes];
    }

    /// <summary>
    /// Returns a PNG byte stream whose signature is valid but which is truncated before the
    /// IHDR width/height fields, so dimension parsing fails.
    /// </summary>
    public static byte[] BuildTruncatedPng()
    {
        var bytes = new List<byte>();
        bytes.AddRange(PngSignature);
        bytes.AddRange([0x00, 0x00, 0x00, 0x0D]); // claims an IHDR chunk follows, but nothing does
        return [.. bytes];
    }

    /// <summary>
    /// Builds a minimal-but-valid baseline JPEG byte stream: SOI + APP0/JFIF header + SOF0
    /// segment carrying the given width/height, matching the marker-walking logic in
    /// ImageUploadValidator.
    /// </summary>
    public static byte[] BuildJpeg(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(JpegSignature); // SOI + APP0 marker
        bytes.AddRange([0x00, 0x10]); // APP0 segment length = 16
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange([0x01, 0x01]); // version
        bytes.Add(0x00); // units
        bytes.AddRange([0x00, 0x01]); // x density
        bytes.AddRange([0x00, 0x01]); // y density
        bytes.Add(0x00); // thumbnail width
        bytes.Add(0x00); // thumbnail height

        bytes.AddRange([0xFF, 0xC0]); // SOF0 marker
        bytes.AddRange([0x00, 0x11]); // SOF0 segment length = 17
        bytes.Add(0x08); // precision
        bytes.AddRange(BigEndianUInt16(height));
        bytes.AddRange(BigEndianUInt16(width));
        bytes.Add(0x03); // number of components
        bytes.AddRange([0x01, 0x11, 0x00]); // component 1 (Y)
        bytes.AddRange([0x02, 0x11, 0x01]); // component 2 (Cb)
        bytes.AddRange([0x03, 0x11, 0x01]); // component 3 (Cr)

        bytes.AddRange([0xFF, 0xD9]); // EOI
        return [.. bytes];
    }

    /// <summary>
    /// Returns a JPEG byte stream whose signature is valid (SOI + APP0/JFIF) but which never
    /// reaches an SOF0/SOF2 marker, so dimension parsing fails.
    /// </summary>
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
        // No SOF0 marker follows — the marker walk will run off the end of the stream.
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
