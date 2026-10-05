using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UsbHeadsetTray;

static class IconRenderer
{
    public static Icon Disconnected() => Render(online: false, level: 0, charging: false);

    // The percentage style only applies while discharging with a known level; offline and charging
    // keep the battery-bar icons so those states stay recognizable.
    public static Icon ForStatus(HeadsetStatus s, bool showPercentage) =>
        showPercentage && s.State == HeadsetState.Online && !s.IsCharging && s.BatteryLevel >= 0
            ? RenderPercentage(s.BatteryLevel)
            : Render(s.State == HeadsetState.Online, s.BatteryLevel, s.IsCharging);

    static Color LevelColor(int level) => level switch
    {
        <= 15 => Color.FromArgb(220, 50, 50),
        <= 35 => Color.FromArgb(220, 150, 0),
        _ => Color.FromArgb(0, 200, 80),
    };

    static Icon Render(bool online, int level, bool charging)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        if (!online)
        {
            DrawBatteryBar(g, Color.FromArgb(110, 110, 110), 0);
        }
        else if (charging)
        {
            DrawBatteryBar(g, Color.FromArgb(0, 200, 80), 100);
            using var font = new Font("Segoe UI Symbol", 7f, FontStyle.Bold);
            g.DrawString("+", font, Brushes.White, 2f, 3f);
        }
        else
        {
            DrawBatteryBar(g, LevelColor(level), level);
        }

        return ToIcon(bmp);
    }

    // Battery body occupies x:0..11 (w=12), y:4..11 (h=8). Nub at x:12..13, y:6..8.
    static void DrawBatteryBar(Graphics g, Color fill, int level)
    {
        using var pen = new Pen(Color.White, 1f);
        g.DrawRectangle(pen, 0f, 4f, 11f, 7f);         // body outline
        g.FillRectangle(Brushes.White, 12f, 6f, 2f, 3f); // nub

        int fillW = (int)(10f * Math.Clamp(level, 0, 100) / 100f);
        if (fillW > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, 1f, 5f, fillW, 5f);
        }
    }

    // Drawn at the system small-icon size (16 px at 100% scaling, larger at higher DPI) so digits stay sharp.
    // White digits on a colored badge read on both light and dark taskbars.
    static Icon RenderPercentage(int level)
    {
        using var bmp = DrawPercentage(level, SystemInformation.SmallIconSize.Width);
        return ToIcon(bmp);
    }

    internal static Bitmap DrawPercentage(int level, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Darker than the bar colors so white digits have enough contrast at 16 px
        var c = LevelColor(level);
        using (var badge = new SolidBrush(Color.FromArgb((int)(c.R * 0.75), (int)(c.G * 0.75), (int)(c.B * 0.75))))
        using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), size / 5f))
            g.FillPath(badge, path);

        var text = Math.Clamp(level, 0, 100).ToString();
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        using var font = FitFont(g, text, size, format);
        g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, size, size), format);

        return bmp;
    }

    // Largest bold font whose text fits the icon width with a pixel of margin on each side ("100" needs a smaller one)
    static Font FitFont(Graphics g, string text, int size, StringFormat format)
    {
        for (float em = size * 0.8f; ; em -= 0.5f)
        {
            var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            if (em <= 6f || g.MeasureString(text, font, PointF.Empty, format).Width <= size - 2)
                return font;
            font.Dispose();
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Icon ToIcon(Bitmap bmp)
    {
        var hIcon = bmp.GetHicon();
        var copy = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return copy;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
}
