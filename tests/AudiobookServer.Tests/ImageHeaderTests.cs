using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests;

public class ImageHeaderTests
{
    /// <summary>A PNG's first 24 bytes: signature, IHDR length and type, width, height.</summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndian32(width));
        bytes.AddRange(BigEndian32(height));
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0 });
        return [.. bytes];
    }

    /// <summary>SOI, an APP0 segment, optionally an APP1 holding a decoy frame header, then SOF.</summary>
    private static byte[] Jpeg(int width, int height, byte sof = 0xC0, bool exifThumbnail = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 });

        if (exifThumbnail)
        {
            // A thumbnail's own SOF0 (16x16) inside APP1. Must be skipped, not read.
            byte[] decoy = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 8, 0x00, 0x10, 0x00, 0x10, 3];
            var length = decoy.Length + 2;
            bytes.AddRange(new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length });
            bytes.AddRange(decoy);
        }

        bytes.AddRange(new byte[] { 0xFF, sof, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3 });
        bytes.AddRange(new byte[12]);
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return [.. bytes];
    }

    private static byte[] BigEndian32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static ImageInfo? Read(byte[] bytes) => ImageHeader.TryRead(new MemoryStream(bytes));

    [Fact]
    public void Reads_png_dimensions() =>
        Assert.Equal(new ImageInfo("png", 1400, 1400), Read(Png(1400, 1400)));

    [Fact]
    public void Reads_baseline_jpeg_dimensions() =>
        Assert.Equal(new ImageInfo("jpeg", 600, 900), Read(Jpeg(600, 900)));

    [Fact]
    public void Reads_progressive_jpeg_dimensions() =>
        Assert.Equal(new ImageInfo("jpeg", 1200, 1800), Read(Jpeg(1200, 1800, sof: 0xC2)));

    [Fact]
    public void Ignores_a_frame_header_inside_an_exif_thumbnail() =>
        Assert.Equal(new ImageInfo("jpeg", 2000, 3000), Read(Jpeg(2000, 3000, exifThumbnail: true)));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 })] // truncated jpeg
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08 })] // scan before any frame
    public void Unreadable_or_unsupported_is_null(byte[] bytes) => Assert.Null(Read(bytes));

    [Fact]
    public void Truncated_png_is_null() => Assert.Null(Read(Png(10, 10)[..20]));
}
