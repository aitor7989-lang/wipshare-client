using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WipShare.Client.Tray;

/// <summary>The tray icon's visual states (design app/tray.html).</summary>
internal enum TrayState { Idle, Recording, Uploading, Failed, NotSetUp }

/// <summary>
/// Composes the tray icon per state from the WipShare mark - an accent rounded
/// square with a bg "hole", plus a state tint and a corner badge - rather than
/// shipping five .ico assets. Built as PNG-backed icons so the antialiased edges
/// keep clean alpha on the taskbar.
/// </summary>
internal static class TrayIconFactory
{
    private static readonly Color Accent = Color.FromArgb(0x5E, 0x6A, 0xD2);
    private static readonly Color Err    = Color.FromArgb(0xE3, 0x5D, 0x6A);
    private static readonly Color Warn   = Color.FromArgb(0xD2, 0x99, 0x22);
    private static readonly Color Muted  = Color.FromArgb(0x78, 0x7C, 0x85);  // = --fg-3
    private static readonly Color BgDot  = Color.FromArgb(0x08, 0x09, 0x0A);

    public static Icon Create(TrayState state)
    {
        const int S = 32;
        using var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var glyphColor = state switch
            {
                TrayState.Recording => Err,
                TrayState.NotSetUp => Muted,
                _ => Accent,
            };

            // rounded-square mark (inset to leave room for the corner badge)
            var glyph = new Rectangle(3, 3, 23, 23);
            using (var path = RoundedRect(glyph, 7))
            using (var brush = new SolidBrush(glyphColor))
                g.FillPath(brush, path);

            // the mark's bg "hole" (top-right)
            using (var hole = new SolidBrush(BgDot))
                g.FillEllipse(hole, glyph.Right - 11, glyph.Top + 4, 7, 7);

            // corner badge for uploading / failed / not-set-up
            Color? badge = state switch
            {
                TrayState.Uploading => Accent,
                TrayState.Failed => Err,
                TrayState.NotSetUp => Warn,
                _ => null,
            };
            if (badge is { } bc)
            {
                var br = new Rectangle(S - 14, S - 14, 13, 13);
                using (var ring = new SolidBrush(BgDot))
                    g.FillEllipse(ring, br.X - 2, br.Y - 2, br.Width + 4, br.Height + 4);
                using (var fill = new SolidBrush(bc))
                    g.FillEllipse(fill, br);

                if (state is TrayState.Failed or TrayState.NotSetUp)
                {
                    using var white = new SolidBrush(Color.White);
                    float cx = br.X + br.Width / 2f;
                    g.FillRectangle(white, cx - 0.9f, br.Y + 3f, 1.8f, 4.6f);
                    g.FillEllipse(white, cx - 1f, br.Y + 8.6f, 2f, 2f);
                }
                else // uploading - a small open ring reads as "in progress"
                {
                    using var pen = new Pen(Color.FromArgb(225, 255, 255, 255), 1.4f);
                    g.DrawArc(pen, br.X + 3f, br.Y + 3f, br.Width - 6f, br.Height - 6f, -90, 280);
                }
            }
        }
        return ToIcon(bmp);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Wraps a 32bpp bitmap into a PNG-backed Icon (clean alpha, no HICON leak).</summary>
    private static Icon ToIcon(Bitmap bmp)
    {
        using var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((short)0);                                   // reserved
            w.Write((short)1);                                   // type = icon
            w.Write((short)1);                                   // image count
            w.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));
            w.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height));
            w.Write((byte)0);                                    // palette
            w.Write((byte)0);                                    // reserved
            w.Write((short)1);                                   // planes
            w.Write((short)32);                                  // bpp
            w.Write(pngBytes.Length);                            // image size
            w.Write(6 + 16);                                     // offset to image
            w.Write(pngBytes);
        }
        ms.Position = 0;
        return new Icon(ms, bmp.Width, bmp.Height);
    }
}
