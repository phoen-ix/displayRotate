using System.Drawing.Drawing2D;
using static RotateIt.NativeMethods;

namespace RotateIt;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;
    private HotkeyWindow? _hotkeyWindow;

    public TrayApp()
    {
        _settings = AppSettings.Load();

        _trayIcon = new NotifyIcon
        {
            Icon = CreateRotationIcon(),
            Text = "RotateIt",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            _trayIcon.ContextMenuStrip = BuildMenu();
            if (e.Button == MouseButtons.Left)
            {
                var showMethod = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                showMethod?.Invoke(_trayIcon, null);
            }
        };

        _settings.RestoreOrientations();
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        _hotkeyWindow?.Dispose();
        _hotkeyWindow = new HotkeyWindow(_settings.Hotkeys);

        if (_hotkeyWindow.FailedBindings.Count > 0)
        {
            var names = string.Join("\n", _hotkeyWindow.FailedBindings.Select(b => $"  {b.FormatHotkey()} — {b.DisplayName}"));
            _trayIcon.BalloonTipTitle = "RotateIt";
            _trayIcon.BalloonTipText = $"Some hotkeys could not be registered (already in use):\n{names}";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private static Icon CreateRotationIcon()
    {
        const int render = 256;
        const int target = 32;
        float cx = render / 2f, cy = render / 2f;

        using var bmp = new Bitmap(render, render);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float r = render * 0.34f;
        float lw = render * 0.065f;
        float headLen = render * 0.14f;
        using var pen = new Pen(Color.White, lw) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(Color.White);

        float gap = 30f;
        float sweep = 180f - gap * 2;

        // Top arc
        g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -90 - sweep / 2, sweep);
        DrawArrowHead(g, brush, cx, cy, r, (-90 + sweep / 2) * MathF.PI / 180, headLen);

        // Bottom arc
        g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 90 - sweep / 2, sweep);
        DrawArrowHead(g, brush, cx, cy, r, (90 + sweep / 2) * MathF.PI / 180, headLen);

        // "R" letter in center
        using var font = new Font("Segoe UI", render * 0.3f, FontStyle.Bold, GraphicsUnit.Pixel);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("R", font, brush, cx, cy + render * 0.02f, sf);

        // Scale down
        using var scaled = new Bitmap(target, target);
        using var sg = Graphics.FromImage(scaled);
        sg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        sg.SmoothingMode = SmoothingMode.AntiAlias;
        sg.DrawImage(bmp, 0, 0, target, target);

        return Icon.FromHandle(scaled.GetHicon());
    }

    private static void DrawArrowHead(Graphics g, Brush brush, float cx, float cy, float r, float angle, float size)
    {
        float tipX = cx + r * MathF.Cos(angle);
        float tipY = cy + r * MathF.Sin(angle);
        float tangent = angle + MathF.PI / 2;
        float spread = 0.45f;

        PointF[] head =
        {
            new(tipX, tipY),
            new(tipX - size * MathF.Cos(tangent - spread), tipY - size * MathF.Sin(tangent - spread)),
            new(tipX - size * MathF.Cos(tangent + spread), tipY - size * MathF.Sin(tangent + spread))
        };
        g.FillPolygon(brush, head);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var displays = DisplayManager.GetDisplays();

        foreach (var display in displays)
        {
            string label = $"Display {display.Index}: {display.FriendlyName}";
            if (display.IsPrimary) label += " (Primary)";
            label += $" [{display.Width}x{display.Height}]";

            var displayItem = new ToolStripMenuItem(label) { Enabled = false };
            menu.Items.Add(displayItem);

            var orientations = new (int Value, string Name)[]
            {
                (DMDO_DEFAULT, "0° — Landscape"),
                (DMDO_90, "90° — Portrait"),
                (DMDO_180, "180° — Landscape flipped"),
                (DMDO_270, "270° — Portrait flipped")
            };

            foreach (var (value, name) in orientations)
            {
                var item = new ToolStripMenuItem(name)
                {
                    Checked = display.CurrentOrientation == value
                };
                string deviceName = display.DeviceName;
                int orientation = value;
                item.Click += (_, _) =>
                {
                    var (success, message) = DisplayManager.Rotate(deviceName, orientation);
                    if (!success)
                        MessageBox.Show(message, "RotateIt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _trayIcon.ContextMenuStrip = BuildMenu();
                };
                menu.Items.Add(item);
            }

            if (_settings.ShowIdentifyButtons)
            {
                var identifyOneItem = new ToolStripMenuItem("Identify");
                string identifyDeviceName = display.DeviceName;
                int identifyIndex = display.Index;
                identifyOneItem.Click += (_, _) => OverlayForm.ShowIdentifyOverlay(identifyIndex, identifyDeviceName, _settings.OverlayDurationMs);
                menu.Items.Add(identifyOneItem);
            }

            menu.Items.Add(new ToolStripSeparator());
        }

        var identifyItem = new ToolStripMenuItem("Identify Displays");
        identifyItem.Click += (_, _) => OverlayForm.ShowIdentifyOverlays(_settings.OverlayDurationMs);
        menu.Items.Add(identifyItem);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += OnOpenSettings;
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        _hotkeyWindow?.Dispose();
        _hotkeyWindow = null;

        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = AppSettings.Load();
            _trayIcon.ContextMenuStrip = BuildMenu();
        }

        RegisterHotkeys();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyWindow?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Dictionary<int, HotkeyBinding> _idToBinding = new();
        private int _registeredCount;

        public List<HotkeyBinding> FailedBindings { get; } = new();

        public HotkeyWindow(List<HotkeyBinding> hotkeys)
        {
            CreateHandle(new CreateParams());

            int id = 1;
            foreach (var binding in hotkeys)
            {
                if (!binding.IsEnabled || binding.VirtualKey == 0)
                    continue;

                uint mods = binding.Modifiers | MOD_NOREPEAT;
                if (RegisterHotKey(Handle, id, mods, binding.VirtualKey))
                {
                    _idToBinding[id] = binding;
                    _registeredCount = id;
                }
                else
                {
                    FailedBindings.Add(binding);
                }
                id++;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && _idToBinding.TryGetValue(m.WParam.ToInt32(), out var binding))
            {
                string? deviceName;
                if (binding.TargetDisplayIndex == 0)
                    deviceName = DisplayManager.GetDisplays().FirstOrDefault(d => d.IsPrimary)?.DeviceName;
                else
                    deviceName = DisplayManager.GetDisplays().FirstOrDefault(d => d.Index == binding.TargetDisplayIndex)?.DeviceName;

                if (deviceName != null)
                    DisplayManager.Rotate(deviceName, binding.TargetOrientation);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            for (int i = 1; i <= _registeredCount; i++)
                UnregisterHotKey(Handle, i);
            DestroyHandle();
        }
    }
}
