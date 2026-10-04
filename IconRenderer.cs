using System.Runtime.InteropServices;

namespace UsbHeadsetTray;

static class IconRenderer
{
    public static Icon Disconnected() => Render(online: false, level: 0, charging: false);

    public static Icon ForStatus(HeadsetStatus s) =>
        Render(s.State == HeadsetState.Online, s.BatteryLevel, s.IsCharging);

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
            var color = level switch
            {
                <= 15 => Color.FromArgb(220, 50, 50),
                <= 35 => Color.FromArgb(220, 150, 0),
                _ => Color.FromArgb(0, 200, 80),
            };
            DrawBatteryBar(g, color, level);
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
