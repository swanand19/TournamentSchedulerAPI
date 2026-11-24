using System.Buffers.Binary;

namespace TournamentScheduler.Api.Pictures;

/// <summary>
/// Is this upload really a picture, and how big is it? Read from the file's own header, never from what the
/// client says it is: only JPEG and PNG are kept (the apps always send JPEG), so nothing else can be stored
/// and served back under an image type.
/// </summary>
public static class PictureFile
{
    public const int MinSide = 64;
    public const int MaxSide = 4096;

    public sealed record Info(string Extension, string ContentType, int Width, int Height);

    /// <summary>The picture's kind and size, or null when it isn't a JPEG or PNG we can read.</summary>
    public static Info? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            return new Info("png", "image/png", width, height);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ReadJpegSize(bytes) is { } size ? new Info("jpg", "image/jpeg", size.Width, size.Height) : null;

        return null;
    }

    /// <summary>A JPEG's size is in its first "start of frame" segment; walk the segments to it.</summary>
    private static (int Width, int Height)? ReadJpegSize(ReadOnlySpan<byte> bytes)
    {
        var i = 2;
        while (i + 9 < bytes.Length)
        {
            if (bytes[i] != 0xFF) return null;
            var marker = bytes[i + 1];
            if (marker == 0xFF) { i++; continue; }          // fill byte
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; } // no length
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 2, 2));
            // SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return (BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 7, 2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 5, 2)));
            if (marker == 0xDA || length < 2) return null; // image data before any frame header
            i += 2 + length;
        }
        return null;
    }
}
