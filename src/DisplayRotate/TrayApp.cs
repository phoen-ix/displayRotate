using System.Drawing.Drawing2D;
using DisplayRotate.Core;
using DisplayRotate.Core.Updates;
using static DisplayRotate.NativeMethods;

namespace DisplayRotate;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;
    private HotkeyWindow? _hotkeyWindow;

    private readonly Updater _updater;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60_000 };
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitRegistration;
    private bool _exiting;

    /// <summary>What clicking the balloon on screen does - only update balloons do anything.</summary>
    private Action? _balloonAction;

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _updater = new Updater(() => _settings);

        _trayIcon = new NotifyIcon
        {
            Icon = CreateRotationIcon(),
            Text = TrayText(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        // The menu strip above is the first control, and creating it installed the WinForms
        // context; the quit event and the update timer both post back through it.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
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

        _trayIcon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };
        _trayIcon.BalloonTipClosed += (_, _) => _balloonAction = null;

        _settings.RestoreOrientations();
        RegisterHotkeys();
        ListenForQuit();

        // Re-point the autostart entry at this exe, in case it moved since it was written.
        if (_settings.StartWithWindows)
            _settings.ApplyStartWithWindows();

        UpdateEngine.SweepOldDownloads(Path.GetTempPath(), DateTime.UtcNow);

        _updater.Changed += () => _trayIcon.Text = TrayText();
        _updater.InstallDidNotHappen += () => ShowBalloon(
            "The update did not install",
            "DisplayRotate is still running the old version. Try again from the tray menu, or download the release and run it.",
            ToolTipIcon.Warning);

        // First check a minute after start, then look hourly whether a day has passed.
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 3_600_000;
            await ScheduledCheckAsync();
        };
        _updateTimer.Start();
    }

    private static string TrayText() =>
        ProductInfo.IsDevBuild ? "DisplayRotate (development build)" : $"DisplayRotate {ProductInfo.Version}";

    private void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonAction = onClick;
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(5000);
    }

    /// <summary>
    /// Waits for the installer to ask this instance to quit (Names.QuitEvent).
    /// </summary>
    /// <remarks>
    /// The installer signals the event, then polls the single-instance mutex until it is gone,
    /// for up to ten seconds, before it falls back to asking the user or force-closing. So the
    /// exit must not wait on anything - not a dialog, not a download.
    /// </remarks>
    private void ListenForQuit()
    {
        try
        {
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Names.QuitEvent);
            _quitRegistration = ThreadPool.RegisterWaitForSingleObject(
                _quitEvent,
                (_, _) => _ui.Post(_ => ExitApp(), null),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;
        _updateTimer.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private async Task ScheduledCheckAsync()
    {
        if (_exiting || !_settings.DailyUpdateCheck
            || !UpdateSchedule.IsCheckDue(_settings.LastUpdateCheckUtc, DateTimeOffset.UtcNow))
            return;

        UpdateCheck? result;
        try
        {
            result = await _updater.CheckAsync();
        }
        catch (Exception)
        {
            // A check nobody asked for right now never interrupts anyone; the next one is an hour away.
            return;
        }

        if (!_exiting && result is { Outcome: CheckOutcome.Available, Latest: { } latest })
        {
            ShowBalloon(
                $"DisplayRotate {latest.ToString(3)} is available",
                "Click here to install it, or use the tray menu.",
                ToolTipIcon.Info,
                () => _ = InstallUpdateAsync());
        }
    }

    private async Task CheckNowAsync()
    {
        var result = await _updater.CheckAsync();
        if (result is null || _exiting)
            return;

        ShowBalloon(
            "Updates",
            Updater.Describe(result) + (result.Outcome == CheckOutcome.Available ? " Click here to install it." : ""),
            result.Outcome == CheckOutcome.Unreachable ? ToolTipIcon.Warning : ToolTipIcon.Info,
            result.Outcome == CheckOutcome.Available ? () => _ = InstallUpdateAsync() : null);
    }

    private async Task InstallUpdateAsync()
    {
        if (_updater.Available is not { } latest || _updater.Busy || _updater.Installing)
            return;

        var install = Updater.DetectInstall();
        if (install.Scope == InstallScope.Portable)
        {
            if (MessageBox.Show(
                    $"DisplayRotate {latest.ToString(3)} is available.\n\nThis copy was not installed, so it can't update itself. Open the download page?",
                    "DisplayRotate", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Updater.OpenReleasePage();
            return;
        }

        var prompt = $"Install DisplayRotate {latest.ToString(3)} now? You have {ProductInfo.Version}.\n\n" +
                     "DisplayRotate closes while the files are replaced, and starts again at the new version." +
                     (install.Scope == InstallScope.PerMachine ? "\n\nIt is installed for everyone on this computer, so Windows will ask for administrator rights." : "");
        if (MessageBox.Show(prompt, "DisplayRotate", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        // Progress arrives on the download's thread, once per 64 KB: post only when the text changes.
        var shown = "";
        var result = await _updater.ApplyAsync((done, total) =>
        {
            var text = Updater.Progress(done, total);
            if (text == Interlocked.Exchange(ref shown, text))
                return;
            _ui.Post(_ => { if (_updater.Busy) _trayIcon.Text = text; }, null);
        });
        if (result is null || _exiting)
            return;

        switch (result.Outcome)
        {
            case ApplyOutcome.Installing:
                // No dialog here: the installer is about to ask this app to quit, and a modal
                // box would hold it up.
                ShowBalloon("Updating DisplayRotate", result.Message, ToolTipIcon.Info);
                break;
            case ApplyOutcome.Declined:
                ShowBalloon("Update cancelled", result.Message, ToolTipIcon.Info);
                break;
            default:
                MessageBox.Show(result.Message, "DisplayRotate update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeyWindow?.Dispose();
        _hotkeyWindow = new HotkeyWindow(_settings.Hotkeys);

        if (_hotkeyWindow.FailedBindings.Count > 0)
        {
            var names = string.Join("\n", _hotkeyWindow.FailedBindings.Select(b => $"  {b.FormatHotkey()} — {b.DisplayName}"));
            ShowBalloon("DisplayRotate", $"Some hotkeys could not be registered (already in use):\n{names}", ToolTipIcon.Warning);
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

        if (_updater.Available is { } latest)
        {
            var installItem = new ToolStripMenuItem($"Install DisplayRotate {latest.ToString(3)}…")
            {
                Font = new Font(menu.Font, FontStyle.Bold),
                Enabled = !_updater.Busy && !_updater.Installing
            };
            installItem.Click += async (_, _) => await InstallUpdateAsync();
            menu.Items.Add(installItem);
            menu.Items.Add(new ToolStripSeparator());
        }

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
                        MessageBox.Show(message, "DisplayRotate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        var checkItem = new ToolStripMenuItem("Check for updates")
        {
            Enabled = !_updater.Busy && !_updater.Installing && !_updater.Engine.DisabledByPolicy
        };
        checkItem.Click += async (_, _) => await CheckNowAsync();
        menu.Items.Add(checkItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        _hotkeyWindow?.Dispose();
        _hotkeyWindow = null;

        using var form = new SettingsForm(_settings, _updater);
        var result = form.ShowDialog();

        // The installer's quit request closes this dialog too; don't come back to life after it.
        if (_exiting)
            return;

        if (result == DialogResult.OK)
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
            _quitRegistration?.Unregister(null);
            _quitEvent?.Dispose();
            _updateTimer.Dispose();
            _updater.Dispose();
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
