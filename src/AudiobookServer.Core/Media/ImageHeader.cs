using System.Buffers.Binary;

namespace AudiobookServer.Core.Media;

/// <summary>An image's format ("jpeg" or "png") and pixel size, read from its header.</summary>
public sealed record ImageInfo(string Format, int Width, int Height);

/// <summary>
/// Reads an image's dimensions from the first few hundred bytes, without decoding it.
///
/// Only jpeg and png: those are what cover files are in practice, and what the cover
/// endpoint serves. Anything else (or a truncated, corrupt file) is null, and the
/// scanner simply doesn't consider it. Done here rather than with ffprobe because it's
/// a few dozen lines, needs no process per image, and is easy to unit-test.
/// </summary>
public static class ImageHeader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageInfo? TryReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryRead(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ImageInfo? TryRead(Stream stream)
    {
        Span<byte> head = stackalloc byte[24];
        if (!ReadExactly(stream, head[..2]))
            return null;

        if (head[0] == 0xFF && head[1] == 0xD8)
            return TryReadJpeg(stream);

        // PNG: signature, then the IHDR chunk (length, "IHDR", width, height), big-endian.
        if (!ReadExactly(stream, head[2..24]) || !head[..8].SequenceEqual(PngSignature))
            return null;
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
            return null;

        var width = BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
        return width > 0 && height > 0 ? new ImageInfo("png", width, height) : null;
    }

    /// <summary>
    /// Walks the jpeg's marker segments until a start-of-frame (SOFn), which holds the
    /// size. EXIF thumbnails live inside APP1, which is skipped whole, so their own SOF
    /// markers are never mistaken for the image's.
    /// </summary>
    private static ImageInfo? TryReadJpeg(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[7];

        while (true)
        {
            // Find the next marker: 0xFF, possibly repeated as fill, then the code.
            int b;
            do { b = stream.ReadByte(); } while (b is not -1 and not 0xFF);
            do { b = stream.ReadByte(); } while (b == 0xFF);
            if (b == -1)
                return null;

            var marker = (byte)b;

            // Markers with no payload.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;

            // End of image, or start of scan with no frame header seen: give up.
            if (marker is 0xD9 or 0xDA)
                return null;

            if (!ReadExactly(stream, buffer[..2]))
                return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer[..2]);
            if (length < 2)
                return null;

            // SOF0-15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                // precision (1), height (2), width (2)
                if (!ReadExactly(stream, buffer[..5]))
                    return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(buffer[1..3]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(buffer[3..5]);
                return width > 0 && height > 0 ? new ImageInfo("jpeg", width, height) : null;
            }

            if (!Skip(stream, length - 2))
                return null;
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static bool Skip(Stream stream, int count)
    {
        if (stream.CanSeek)
        {
            if (stream.Position + count > stream.Length)
                return false;
            stream.Seek(count, SeekOrigin.Current);
            return true;
        }

        Span<byte> sink = stackalloc byte[256];
        while (count > 0)
        {
            var read = stream.Read(sink[..Math.Min(count, sink.Length)]);
            if (read == 0)
                return false;
            count -= read;
        }
        return true;
    }
}
