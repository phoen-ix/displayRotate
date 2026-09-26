namespace DisplayRotate;

internal sealed class OverlayForm : Form
{
    private readonly int _displayNumber;
    private readonly System.Windows.Forms.Timer _timer;

    public OverlayForm(int displayNumber, Screen screen, int durationMs)
    {
        _displayNumber = displayNumber;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.75;
        Bounds = screen.Bounds;
        DoubleBuffered = true;

        _timer = new System.Windows.Forms.Timer { Interval = durationMs };
        _timer.Tick += (_, _) => Close();
        _timer.Start();

        Click += (_, _) => Close();
        KeyDown += (_, _) => Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var font = new Font("Segoe UI", Math.Min(ClientSize.Width, ClientSize.Height) * 0.4f, FontStyle.Bold);
        var text = _displayNumber.ToString();
        var size = e.Graphics.MeasureString(text, font);
        float x = (ClientSize.Width - size.Width) / 2;
        float y = (ClientSize.Height - size.Height) / 2;
        e.Graphics.DrawString(text, font, Brushes.White, x, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }

    public static void ShowIdentifyOverlays(int durationMs)
    {
        var displays = DisplayManager.GetDisplays();
        foreach (var display in displays)
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == display.DeviceName);
            if (screen == null) continue;
            new OverlayForm(display.Index, screen, durationMs).Show();
        }
    }

    public static void ShowIdentifyOverlay(int displayIndex, string deviceName, int durationMs)
    {
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName);
        if (screen == null) return;
        new OverlayForm(displayIndex, screen, durationMs).Show();
    }
}
