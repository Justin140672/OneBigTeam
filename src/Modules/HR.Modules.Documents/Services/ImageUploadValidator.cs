using HR.SharedKernel;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

internal sealed class ImageUploadValidator : IImageUploadValidator
{
    // Maps a declared content type to the magic byte sequences that identify it.
    // Reuses the same signature knowledge as FileUploadValidator, scoped to image types.
    private static readonly Dictionary<string, byte[][]> MagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] =
        [
            [0xFF, 0xD8, 0xFF, 0xE0], // JFIF
            [0xFF, 0xD8, 0xFF, 0xE1], // EXIF
            [0xFF, 0xD8, 0xFF, 0xE8], // SPIFF
            [0xFF, 0xD8, 0xFF, 0xDB], // raw JPEG
        ],
        ["image/png"] =
        [
            [0x89, 0x50, 0x4E, 0x47], // ‰PNG
        ],
    };

    private readonly ImageUploadOptions _options;

    public ImageUploadValidator(IOptions<ImageUploadOptions> options)
    {
        _options = options.Value;
    }

    public Result Validate(string fileName, string contentType, long fileSize)
    {
        if (fileSize <= 0)
            return Result.Failure(Error.Validation("File must not be empty."));

        if (fileSize > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation($"File size exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) ||
            !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", _options.AllowedExtensions);
            return Result.Failure(Error.Validation($"File type '{extension}' is not allowed. Allowed types: {allowed}."));
        }

        var normalizedContentType = contentType.Split(';')[0].Trim();
        if (!_options.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", _options.AllowedContentTypes);
            return Result.Failure(Error.Validation($"Content type '{normalizedContentType}' is not allowed. Allowed types: {allowed}."));
        }

        return Result.Success();
    }

    public Result ValidateImageContent(Stream content, string contentType)
    {
        var normalizedContentType = contentType.Split(';')[0].Trim();

        if (!MagicBytes.TryGetValue(normalizedContentType, out var signatures))
            return Result.Failure(Error.Validation($"Unsupported image content type '{normalizedContentType}'."));

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (bytes.Length < 8)
            return Result.Failure(Error.Validation("File content is too short to be a valid image."));

        var matchesSignature = false;
        foreach (var sig in signatures)
        {
            if (bytes.Length >= sig.Length && bytes.AsSpan(0, sig.Length).SequenceEqual(sig))
            {
                matchesSignature = true;
                break;
            }
        }

        if (!matchesSignature)
            return Result.Failure(Error.Validation(
                $"File content does not match the declared type '{normalizedContentType}'. " +
                "Ensure the file has not been renamed or tampered with."));

        var dimensions = normalizedContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            ? TryGetPngDimensions(bytes)
            : TryGetJpegDimensions(bytes);

        if (dimensions is null)
            return Result.Failure(Error.Validation("Unable to determine image dimensions."));

        var (width, height) = dimensions.Value;

        if (width < _options.MinWidthPx || height < _options.MinHeightPx)
            return Result.Failure(Error.Validation(
                $"Image dimensions ({width}x{height}) are smaller than the minimum allowed size of " +
                $"{_options.MinWidthPx}x{_options.MinHeightPx}."));

        if (width > _options.MaxWidthPx || height > _options.MaxHeightPx)
            return Result.Failure(Error.Validation(
                $"Image dimensions ({width}x{height}) exceed the maximum allowed size of " +
                $"{_options.MaxWidthPx}x{_options.MaxHeightPx}."));

        return Result.Success();
    }

    // PNG layout: 8-byte signature, 4-byte IHDR chunk length, 4-byte chunk type ("IHDR"),
    // then a big-endian uint32 width followed by a big-endian uint32 height.
    private static (int Width, int Height)? TryGetPngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24)
            return null;

        if (bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
            return null;

        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

        return (width, height);
    }

    // JPEG layout: walk the marker stream from the SOI (0xFFD8) looking for a baseline (0xFFC0)
    // or progressive (0xFFC2) Start-Of-Frame marker. Each intermediate marker segment is skipped
    // using its own declared 2-byte length. The SOF segment holds a 1-byte precision followed by
    // a big-endian uint16 height and a big-endian uint16 width.
    private static (int Width, int Height)? TryGetJpegDimensions(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return null;

        var pos = 2;

        while (pos + 1 < bytes.Length)
        {
            if (bytes[pos] != 0xFF)
                return null;

            while (pos < bytes.Length && bytes[pos] == 0xFF)
                pos++;

            if (pos >= bytes.Length)
                return null;

            var marker = bytes[pos];
            pos++;

            if (marker == 0xD9) // EOI reached without finding SOF
                return null;

            // Markers with no payload/length field.
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            if (pos + 1 >= bytes.Length)
                return null;

            if (marker == 0xC0 || marker == 0xC2)
            {
                if (pos + 6 >= bytes.Length)
                    return null;

                var height = (bytes[pos + 3] << 8) | bytes[pos + 4];
                var width  = (bytes[pos + 5] << 8) | bytes[pos + 6];
                return (width, height);
            }

            var segmentLength = (bytes[pos] << 8) | bytes[pos + 1];
            pos += segmentLength;
        }

        return null;
    }
}
