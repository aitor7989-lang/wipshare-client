using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WipShare.Client.Settings;

namespace WipShare.Client.Capture;

/// <summary>
/// Turns a tightly-packed BGRA frame buffer into a downscaled JPEG poster
/// thumbnail. Best-effort: returns false (and logs) on any failure - a missing
/// thumbnail must never block the recording or its upload.
///
/// Uses System.Drawing (already in the dependency graph via the tray icon and
/// proven on this Windows-only target) rather than the WinRT BitmapEncoder
/// quality path, which is fiddlier for JPEG quality control.
/// </summary>
public static class ThumbnailGenerator
{
    public static bool TryGenerate(byte[] bgra, int width, int height, string outputJpgPath, ILogger logger)
    {
        try
        {
            if (width <= 0 || height <= 0)
            {
                logger.LogWarning("Thumbnail skipped: invalid dimensions {W}x{H}", width, height);
                return false;
            }

            int srcStride = width * 4;
            int expected = srcStride * height;
            if (bgra.Length < expected)
            {
                logger.LogWarning("Thumbnail skipped: buffer {Len} < expected {Exp} for {W}x{H}",
                    bgra.Length, expected, width, height);
                return false;
            }

            using var src = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bits = src.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                // BGRA byte order matches 32bppArgb in memory on little-endian Windows.
                // Bitmap stride can be padded, so copy per-row when it differs.
                if (bits.Stride == srcStride)
                {
                    Marshal.Copy(bgra, 0, bits.Scan0, expected);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(bgra, y * srcStride, IntPtr.Add(bits.Scan0, y * bits.Stride), srcStride);
                }
            }
            finally
            {
                src.UnlockBits(bits);
            }

            double scale = Math.Min(1.0, (double)AppSettings.ThumbnailMaxEdge / Math.Max(width, height));
            int outW = Math.Max(1, (int)Math.Round(width * scale));
            int outH = Math.Max(1, (int)Math.Round(height * scale));

            using var dst = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, outW, outH);
            }

            var jpegCodec = GetJpegCodec();
            if (jpegCodec is null)
            {
                logger.LogWarning("Thumbnail skipped: no JPEG codec registered");
                return false;
            }

            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);

            Directory.CreateDirectory(Path.GetDirectoryName(outputJpgPath)!);
            dst.Save(outputJpgPath, jpegCodec, encoderParams);

            logger.LogInformation("Thumbnail saved: {Path} ({W}x{H} → {OW}x{OH})",
                outputJpgPath, width, height, outW, outH);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Thumbnail generation failed for {Path}; continuing without it", outputJpgPath);
            return false;
        }
    }

    private static ImageCodecInfo? GetJpegCodec()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
        }
        return null;
    }
}
