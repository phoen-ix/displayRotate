using DisplayRotate.Core;
using DisplayRotate.Core.Updates;
using static DisplayRotate.NativeMethods;

namespace DisplayRotate;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly List<HotkeyRow> _hotkeyRows = new();
    private readonly CheckBox _chkStartWithWindows;
    private readonly CheckBox _chkShowIdentify;
    private readonly CheckBox _chkAutoRestore;
    private readonly NumericUpDown _nudOverlayDuration;
    private readonly Panel _orientationsPanel;
    private readonly Panel _hotkeysPanel;
    private readonly List<(string DeviceName, ComboBox Combo)> _orientationCombos = new();

    private readonly Updater _updater;
    private readonly InstallRecord _install = Updater.DetectInstall();
    private readonly RadioButton _rbUpdateManual;
    private readonly RadioButton _rbUpdateDaily;
    private readonly Button _btnCheckNow;
    private readonly Button _btnUpdate;
    private readonly Label _lblUpdateStatus;

    public SettingsForm(AppSettings settings, Updater updater)
    {
        _settings = settings;
        _updater = updater;

        Text = ProductInfo.IsDevBuild
            ? "DisplayRotate Settings — development build"
            : $"DisplayRotate Settings — v{ProductInfo.Version}";
        Size = new Size(580, 660);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);

        var mainPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        Controls.Add(mainPanel);

        int y = 12;

        // --- General ---
        var lblGeneral = new Label { Text = "General", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(12, y), AutoSize = true };
        mainPanel.Controls.Add(lblGeneral);
        y += 28;

        _chkStartWithWindows = new CheckBox { Text = "Start with Windows", Location = new Point(16, y), AutoSize = true, Checked = settings.StartWithWindows };
        mainPanel.Controls.Add(_chkStartWithWindows);
        y += 28;

        _chkShowIdentify = new CheckBox { Text = "Show Identify buttons in context menu", Location = new Point(16, y), AutoSize = true, Checked = settings.ShowIdentifyButtons };
        mainPanel.Controls.Add(_chkShowIdentify);
        y += 28;

        _chkAutoRestore = new CheckBox { Text = "Auto-restore display orientations on startup", Location = new Point(16, y), AutoSize = true, Checked = settings.AutoRestoreOrientations };
        mainPanel.Controls.Add(_chkAutoRestore);
        y += 28;

        var lblDuration = new Label { Text = "Identify overlay duration (seconds):", Location = new Point(16, y + 2), AutoSize = true };
        mainPanel.Controls.Add(lblDuration);
        _nudOverlayDuration = new NumericUpDown { Minimum = 1, Maximum = 5, Value = Math.Clamp(settings.OverlayDurationMs / 1000m, 1, 5), Location = new Point(270, y), Width = 60 };
        mainPanel.Controls.Add(_nudOverlayDuration);
        y += 32;

        // --- Preferred Orientations ---
        _orientationsPanel = new Panel { Location = new Point(12, y), Size = new Size(530, 10), Visible = settings.AutoRestoreOrientations };
        mainPanel.Controls.Add(_orientationsPanel);
        BuildOrientationsPanel();
        y += _orientationsPanel.Visible ? _orientationsPanel.Height + 8 : 0;
        _chkAutoRestore.CheckedChanged += (_, _) => _orientationsPanel!.Visible = _chkAutoRestore.Checked;

        // --- Hotkeys ---
        var lblHotkeys = new Label { Text = "Hotkeys", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(12, y), AutoSize = true };
        mainPanel.Controls.Add(lblHotkeys);
        y += 26;

        _hotkeysPanel = new Panel { Location = new Point(12, y), Size = new Size(530, 200), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
        mainPanel.Controls.Add(_hotkeysPanel);
        BuildHotkeyRows();
        y += 208;

        var btnAdd = new Button { Text = "Add", Location = new Point(12, y), Width = 75 };
        btnAdd.Click += OnAddHotkey;
        mainPanel.Controls.Add(btnAdd);

        var btnRemove = new Button { Text = "Remove", Location = new Point(92, y), Width = 75 };
        btnRemove.Click += OnRemoveHotkey;
        mainPanel.Controls.Add(btnRemove);

        var btnReset = new Button { Text = "Reset to Defaults", Location = new Point(172, y), Width = 120 };
        btnReset.Click += OnResetDefaults;
        mainPanel.Controls.Add(btnReset);
        y += 36;

        // --- Updates ---
        var lblUpdates = new Label { Text = "Updates", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(12, y), AutoSize = true };
        mainPanel.Controls.Add(lblUpdates);
        y += 28;

        // Their own panel, so the two radios form a group of their own.
        var updateModePanel = new Panel { Location = new Point(12, y), Size = new Size(530, 26) };
        _rbUpdateManual = new RadioButton { Text = "Only when I ask", Location = new Point(4, 2), AutoSize = true, Checked = !settings.DailyUpdateCheck };
        _rbUpdateDaily = new RadioButton { Text = "Check once a day", Location = new Point(160, 2), AutoSize = true, Checked = settings.DailyUpdateCheck };
        updateModePanel.Controls.Add(_rbUpdateManual);
        updateModePanel.Controls.Add(_rbUpdateDaily);
        mainPanel.Controls.Add(updateModePanel);
        y += 30;

        _btnCheckNow = new Button { Text = "Check now", Location = new Point(16, y), Width = 100, FlatStyle = FlatStyle.System };
        _btnCheckNow.Click += async (_, _) => await OnCheckNowAsync();
        mainPanel.Controls.Add(_btnCheckNow);

        _btnUpdate = new Button { Location = new Point(122, y), Width = 170, FlatStyle = FlatStyle.System, Visible = false };
        _btnUpdate.Click += async (_, _) => await OnUpdateAsync();
        mainPanel.Controls.Add(_btnUpdate);
        y += 32;

        _lblUpdateStatus = new Label { Location = new Point(16, y), AutoSize = true, MaximumSize = new Size(520, 0) };
        mainPanel.Controls.Add(_lblUpdateStatus);
        y += 44;

        _lblUpdateStatus.Text = InitialUpdateStatus();
        RefreshUpdateControls();
        _updater.Changed += RefreshUpdateControls;
        FormClosed += (_, _) => _updater.Changed -= RefreshUpdateControls;

        // --- Save / Cancel ---
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 45 };
        Controls.Add(buttonPanel);

        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 80, Location = new Point(390, 8) };
        btnSave.Click += OnSave;
        buttonPanel.Controls.Add(btnSave);

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, Location = new Point(476, 8) };
        buttonPanel.Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private string InitialUpdateStatus()
    {
        if (_updater.Engine.DisabledByPolicy)
            return "Update checks are disabled by policy on this computer.";
        if (_updater.Installing)
            return "Installing… DisplayRotate closes while the files are replaced, and starts again at the new version.";
        if (_updater.Last is { } last)
            return Updater.Describe(last);
        return _settings.LastUpdateCheckUtc is { } stamp
            ? $"Last checked {stamp.ToLocalTime():yyyy-MM-dd HH:mm}."
            : "Never checked.";
    }

    private void RefreshUpdateControls()
    {
        if (IsDisposed)
            return;

        var idle = !_updater.Busy && !_updater.Installing;
        _btnCheckNow.Enabled = idle && !_updater.Engine.DisabledByPolicy;

        if (_updater.Available is { } latest && !_updater.Installing)
        {
            var portable = _install.Scope == InstallScope.Portable;
            _btnUpdate.Text = portable ? "Open download page" : $"Update to {latest.ToString(3)}";
            _btnUpdate.Enabled = idle;
            _btnUpdate.Visible = true;

            // The shield says "this asks for administrator rights" before anyone presses it.
            if (_install.Scope == InstallScope.PerMachine && !Environment.IsPrivilegedProcess)
                SendMessage(_btnUpdate.Handle, BCM_SETSHIELD, IntPtr.Zero, 1);
        }
        else
        {
            _btnUpdate.Visible = false;
        }
    }

    private async Task OnCheckNowAsync()
    {
        _lblUpdateStatus.Text = "Checking…";
        var result = await _updater.CheckAsync();
        if (result is not null && !IsDisposed)
            _lblUpdateStatus.Text = Updater.Describe(result);
    }

    private async Task OnUpdateAsync()
    {
        if (_install.Scope == InstallScope.Portable)
        {
            Updater.OpenReleasePage();
            return;
        }

        _lblUpdateStatus.Text = "Downloading…";

        // Progress arrives on the download's thread, once per 64 KB: post only when the text changes.
        var shown = "";
        var result = await _updater.ApplyAsync((done, total) =>
        {
            var text = Updater.Progress(done, total);
            if (text != Interlocked.Exchange(ref shown, text) && IsHandleCreated)
                BeginInvoke(() => { if (_updater.Busy) _lblUpdateStatus.Text = text; });
        });

        if (result is null || IsDisposed)
            return;

        _lblUpdateStatus.Text = result.Message;

        // No dialog while installing: the installer is about to close this window along with
        // the app, and gives it only seconds to go.
        if (result.Outcome is ApplyOutcome.Failed or ApplyOutcome.DisabledByPolicy)
            MessageBox.Show(this, result.Message, "DisplayRotate update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void BuildOrientationsPanel()
    {
        _orientationsPanel.Controls.Clear();
        _orientationCombos.Clear();
        var displays = DisplayManager.GetDisplays();
        int oy = 4;
        foreach (var d in displays)
        {
            var lbl = new Label { Text = $"Display {d.Index}: {d.FriendlyName}", Location = new Point(4, oy + 3), AutoSize = true };
            _orientationsPanel.Controls.Add(lbl);

            var btnIdentify = new Button { Text = "Identify", Location = new Point(240, oy), Width = 55, Height = 24 };
            int idx = d.Index;
            string devName = d.DeviceName;
            int dur = _settings.OverlayDurationMs;
            btnIdentify.Click += (_, _) => OverlayForm.ShowIdentifyOverlay(idx, devName, dur);
            _orientationsPanel.Controls.Add(btnIdentify);

            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(300, oy), Width = 170 };
            combo.Items.AddRange(new object[] { "0° (Landscape)", "90° (Portrait)", "180° (Landscape flipped)", "270° (Portrait flipped)" });
            int savedOrientation = _settings.PreferredOrientations.GetValueOrDefault(d.DeviceName, d.CurrentOrientation);
            combo.SelectedIndex = Math.Clamp(savedOrientation, 0, 3);
            _orientationsPanel.Controls.Add(combo);
            _orientationCombos.Add((d.DeviceName, combo));
            oy += 30;
        }
        _orientationsPanel.Height = oy + 4;
    }

    private void BuildHotkeyRows()
    {
        _hotkeysPanel.Controls.Clear();
        _hotkeyRows.Clear();

        int ry = 2;
        foreach (var binding in _settings.Hotkeys)
        {
            var row = new HotkeyRow(binding, ry, _hotkeysPanel);
            _hotkeyRows.Add(row);
            ry += 30;
        }
    }

    private void OnAddHotkey(object? sender, EventArgs e)
    {
        var binding = new HotkeyBinding
        {
            ActionId = $"custom_{_settings.Hotkeys.Count}",
            DisplayName = "New Hotkey",
            TargetDisplayIndex = 0,
            TargetOrientation = DMDO_DEFAULT,
            IsEnabled = true
        };
        _settings.Hotkeys.Add(binding);
        var row = new HotkeyRow(binding, _hotkeyRows.Count * 30 + 2, _hotkeysPanel);
        _hotkeyRows.Add(row);
    }

    private void OnRemoveHotkey(object? sender, EventArgs e)
    {
        var selected = _hotkeyRows.FindIndex(r => r.IsSelected);
        if (selected < 0) return;

        _settings.Hotkeys.RemoveAt(selected);
        BuildHotkeyRows();
    }

    private void OnResetDefaults(object? sender, EventArgs e)
    {
        var defaults = AppSettings.CreateDefault();
        _settings.Hotkeys.Clear();
        _settings.Hotkeys.AddRange(defaults.Hotkeys);
        BuildHotkeyRows();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _settings.StartWithWindows = _chkStartWithWindows.Checked;
        _settings.ShowIdentifyButtons = _chkShowIdentify.Checked;
        _settings.AutoRestoreOrientations = _chkAutoRestore.Checked;
        _settings.OverlayDurationMs = (int)_nudOverlayDuration.Value * 1000;
        _settings.UpdateCheck = _rbUpdateDaily.Checked ? AppSettings.UpdateCheckDaily : AppSettings.UpdateCheckManual;

        _settings.PreferredOrientations.Clear();
        foreach (var (deviceName, combo) in _orientationCombos)
            _settings.PreferredOrientations[deviceName] = combo.SelectedIndex;

        foreach (var row in _hotkeyRows)
            row.ApplyToBinding();

        _settings.Save();
        _settings.ApplyStartWithWindows();
    }

    private void LayoutForm()
    {
        // Let the panel auto-scroll handle repositioning
    }

    private sealed class HotkeyRow
    {
        private readonly HotkeyBinding _binding;
        private readonly CheckBox _chkEnabled;
        private readonly ComboBox _cmbDisplay;
        private readonly ComboBox _cmbOrientation;
        private readonly TextBox _txtHotkey;
        private uint _modifiers;
        private uint _virtualKey;

        public bool IsSelected => _txtHotkey.Focused || _chkEnabled.Focused || _cmbDisplay.Focused || _cmbOrientation.Focused;

        public HotkeyRow(HotkeyBinding binding, int y, Panel parent)
        {
            _binding = binding;
            _modifiers = binding.Modifiers;
            _virtualKey = binding.VirtualKey;

            _chkEnabled = new CheckBox { Location = new Point(4, y + 3), AutoSize = true, Checked = binding.IsEnabled };
            parent.Controls.Add(_chkEnabled);

            _cmbDisplay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(28, y), Width = 100 };
            _cmbDisplay.Items.Add("Primary");
            var displays = DisplayManager.GetDisplays();
            foreach (var d in displays)
                _cmbDisplay.Items.Add($"Display {d.Index}");
            _cmbDisplay.SelectedIndex = Math.Min(binding.TargetDisplayIndex, _cmbDisplay.Items.Count - 1);
            parent.Controls.Add(_cmbDisplay);

            _cmbOrientation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(134, y), Width = 160 };
            _cmbOrientation.Items.AddRange(new object[] { "0° (Landscape)", "90° (Portrait)", "180° (Landscape flipped)", "270° (Portrait flipped)" });
            _cmbOrientation.SelectedIndex = Math.Clamp(binding.TargetOrientation, 0, 3);
            parent.Controls.Add(_cmbOrientation);

            _txtHotkey = new TextBox
            {
                Location = new Point(300, y),
                Width = 200,
                ReadOnly = true,
                Text = binding.FormatHotkey(),
                BackColor = SystemColors.Window,
                Cursor = Cursors.Hand
            };
            _txtHotkey.KeyDown += OnHotkeyKeyDown;
            _txtHotkey.GotFocus += (_, _) => _txtHotkey.BackColor = Color.LightYellow;
            _txtHotkey.LostFocus += (_, _) => _txtHotkey.BackColor = SystemColors.Window;
            parent.Controls.Add(_txtHotkey);
        }

        private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            if (e.KeyCode == Keys.Escape)
            {
                _modifiers = 0;
                _virtualKey = 0;
                _txtHotkey.Text = "(None)";
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                _modifiers = 0;
                _virtualKey = 0;
                _txtHotkey.Text = "(None)";
                return;
            }

            if (!e.Control && !e.Alt && !e.Shift)
                return;

            uint mods = 0;
            if (e.Control) mods |= MOD_CONTROL;
            if (e.Alt) mods |= MOD_ALT;
            if (e.Shift) mods |= MOD_SHIFT;

            _modifiers = mods;
            _virtualKey = (uint)e.KeyCode;

            _binding.Modifiers = _modifiers;
            _binding.VirtualKey = _virtualKey;
            _txtHotkey.Text = _binding.FormatHotkey();
        }

        public void ApplyToBinding()
        {
            _binding.IsEnabled = _chkEnabled.Checked;
            _binding.TargetDisplayIndex = _cmbDisplay.SelectedIndex;
            _binding.TargetOrientation = _cmbOrientation.SelectedIndex;
            _binding.Modifiers = _modifiers;
            _binding.VirtualKey = _virtualKey;

            string displayLabel = _cmbDisplay.SelectedIndex == 0 ? "Primary" : $"Display {_cmbDisplay.SelectedIndex}";
            string orientLabel = DisplayManager.GetOrientationName(_cmbOrientation.SelectedIndex);
            _binding.DisplayName = $"Rotate {displayLabel} to {orientLabel}";
            _binding.ActionId = $"rotate_{displayLabel.ToLower().Replace(" ", "")}_{_cmbOrientation.SelectedIndex}";
        }
    }
}
