using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using LogiBatteryTray.Models;
using LogiBatteryTray.Rendering;
using LogiBatteryTray.Services;

namespace LogiBatteryTray.UI;

/// <summary>
/// Modern borderless Windows 11 flyout displaying detailed battery cards and quick settings.
/// </summary>
public sealed class BatteryFlyoutForm : Form
{
    private readonly Label _titleLabel;
    private readonly Button _refreshButton;
    private readonly Panel _cardsHost;
    private readonly Panel _divider;
    private readonly Label _modeLabel;
    private readonly Button _modeSplitButton;
    private readonly Button _modeNumbersButton;
    private readonly Button _modeCyclingButton;
    private readonly Button _modeCombinedButton;
    private readonly CheckBox _autostartCheckBox;
    private readonly CheckBox _alertCheckBox;
    private readonly LinkLabel _taskbarLink;
    private readonly Button _exitButton;

    private readonly Font _titleFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _bodyFont = new("Segoe UI", 9f, FontStyle.Regular);
    private readonly Font _buttonFont = new("Segoe UI", 8.75f, FontStyle.Regular);
    private readonly Font _linkFont = new("Segoe UI", 8.5f, FontStyle.Regular);

    private IReadOnlyList<DeviceBatteryInfo> _devices = [];
    private AppSettings _settings = new();
    private bool _autostartEnabled;
    private bool _suppressEvents;
    private bool _isLightTheme = true;

    private Color _bgColor;
    private Color _cardColor;
    private Color _cardBorderColor;
    private Color _borderColor;
    private Color _textPrimary;
    private Color _textSecondary;
    private Color _buttonBg;
    private Color _trackColor;

    public DateTime LastDeactivatedUtc { get; private set; } = DateTime.MinValue;

    public event EventHandler? RefreshRequested;
    public event EventHandler<IconDisplayMode>? IconModeSelected;
    public event EventHandler<(DeviceBatteryInfo Device, bool Visible)>? DeviceVisibilityChanged;
    public event EventHandler<bool>? AutostartChanged;
    public event EventHandler<bool>? LowBatteryAlertChanged;
    public event EventHandler? OpenTaskbarSettingsRequested;
    public event EventHandler? ExitRequested;

    public BatteryFlyoutForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            Text = "Logitech Akku",
            AutoSize = true
        };

        _refreshButton = new Button
        {
            Text = "Aktualisieren",
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _refreshButton.FlatAppearance.BorderSize = 1;
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _cardsHost = new Panel
        {
            BackColor = Color.Transparent
        };

        _divider = new Panel();

        _modeLabel = new Label
        {
            Text = "Darstellung:",
            AutoSize = true
        };

        _modeSplitButton = CreateModeButton("Mit Icon", IconDisplayMode.Split);
        _modeNumbersButton = CreateModeButton("Nur Prozent", IconDisplayMode.NumbersOnly);
        _modeCyclingButton = CreateModeButton("1 Symbol (Wechsel)", IconDisplayMode.Cycling);
        _modeCombinedButton = CreateModeButton("1 Symbol (Kombi)", IconDisplayMode.Combined);

        _autostartCheckBox = new CheckBox
        {
            Text = "Mit Windows starten",
            AutoSize = true,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _autostartCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressEvents)
            {
                AutostartChanged?.Invoke(this, _autostartCheckBox.Checked);
            }
        };

        _alertCheckBox = new CheckBox
        {
            Text = "Akku-Warnung unter 20 %",
            AutoSize = true,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _alertCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressEvents)
            {
                LowBatteryAlertChanged?.Invoke(this, _alertCheckBox.Checked);
            }
        };

        _taskbarLink = new LinkLabel
        {
            Text = "Taskleistensymbole anheften...",
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TabStop = false
        };
        _taskbarLink.LinkClicked += (_, _) => OpenTaskbarSettingsRequested?.Invoke(this, EventArgs.Empty);

        _exitButton = new Button
        {
            Text = "Beenden",
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _exitButton.FlatAppearance.BorderSize = 1;
        _exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(
        [
            _titleLabel,
            _refreshButton,
            _cardsHost,
            _divider,
            _modeLabel,
            _modeSplitButton,
            _modeNumbersButton,
            _modeCyclingButton,
            _modeCombinedButton,
            _autostartCheckBox,
            _alertCheckBox,
            _taskbarLink,
            _exitButton
        ]);

        ApplyTheme();
        RebuildLayout();
    }

    public void UpdateState(IReadOnlyList<DeviceBatteryInfo> devices, AppSettings settings, bool autostartEnabled)
    {
        _devices = devices;
        _settings = settings;
        _autostartEnabled = autostartEnabled;

        if (Visible)
        {
            ApplyTheme();
            RebuildLayout();
        }
    }

    public void ShowNearTray()
    {
        ApplyTheme();
        RebuildLayout();

        Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        int margin = S(12);
        int x = Math.Clamp(workArea.Right - Width - margin, workArea.Left + margin, workArea.Right - Width - margin);
        int y = Math.Clamp(workArea.Bottom - Height - margin, workArea.Top + margin, workArea.Bottom - Height - margin);
        Location = new Point(x, y);

        if (!Visible)
        {
            Show();
        }

        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowAttributes();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        LastDeactivatedUtc = DateTime.UtcNow;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_borderColor, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _bodyFont.Dispose();
            _buttonFont.Dispose();
            _linkFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyTheme()
    {
        _isLightTheme = ThemeHelper.IsAppsLightTheme();

        if (_isLightTheme)
        {
            _bgColor = Color.FromArgb(245, 246, 248);
            _cardColor = Color.White;
            _cardBorderColor = Color.FromArgb(228, 228, 231);
            _borderColor = Color.FromArgb(212, 212, 216);
            _textPrimary = Color.FromArgb(24, 24, 27);
            _textSecondary = Color.FromArgb(101, 101, 110);
            _buttonBg = Color.White;
            _trackColor = Color.FromArgb(228, 228, 231);
            _taskbarLink.LinkColor = Color.FromArgb(0, 103, 192);
            _taskbarLink.ActiveLinkColor = Color.FromArgb(0, 82, 154);
        }
        else
        {
            _bgColor = Color.FromArgb(32, 32, 36);
            _cardColor = Color.FromArgb(44, 44, 49);
            _cardBorderColor = Color.FromArgb(63, 63, 70);
            _borderColor = Color.FromArgb(68, 68, 76);
            _textPrimary = Color.FromArgb(244, 244, 245);
            _textSecondary = Color.FromArgb(161, 161, 170);
            _buttonBg = Color.FromArgb(52, 52, 58);
            _trackColor = Color.FromArgb(72, 72, 80);
            _taskbarLink.LinkColor = Color.FromArgb(96, 180, 255);
            _taskbarLink.ActiveLinkColor = Color.FromArgb(140, 200, 255);
        }

        BackColor = _bgColor;
        _titleLabel.ForeColor = _textPrimary;
        _divider.BackColor = _cardBorderColor;
        _modeLabel.ForeColor = _textSecondary;
        _autostartCheckBox.ForeColor = _textPrimary;
        _alertCheckBox.ForeColor = _textPrimary;

        StyleButton(_refreshButton);
        StyleModeButtons();
        StyleButton(_exitButton);

        if (IsHandleCreated)
        {
            ApplyWindowAttributes();
        }
    }

    private Button CreateModeButton(string label, IconDisplayMode mode)
    {
        var btn = new Button
        {
            Text = label,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.Click += (_, _) => IconModeSelected?.Invoke(this, mode);
        return btn;
    }

    private void StyleModeButtons()
    {
        StyleModeButton(_modeSplitButton, _settings.IconMode == IconDisplayMode.Split);
        StyleModeButton(_modeNumbersButton, _settings.IconMode == IconDisplayMode.NumbersOnly);
        StyleModeButton(_modeCyclingButton, _settings.IconMode == IconDisplayMode.Cycling);
        StyleModeButton(_modeCombinedButton, _settings.IconMode == IconDisplayMode.Combined);
    }

    private void StyleModeButton(Button btn, bool active)
    {
        if (active)
        {
            Color accent = _isLightTheme ? Color.FromArgb(0, 103, 192) : Color.FromArgb(76, 194, 255);
            btn.BackColor = accent;
            btn.ForeColor = _isLightTheme ? Color.White : Color.FromArgb(17, 17, 17);
            btn.FlatAppearance.BorderColor = accent;
            btn.FlatAppearance.MouseOverBackColor = accent;
        }
        else
        {
            StyleButton(btn);
        }
    }

    private void StyleButton(Button btn)
    {
        btn.BackColor = _buttonBg;
        btn.ForeColor = _textPrimary;
        btn.FlatAppearance.BorderColor = _cardBorderColor;
        btn.FlatAppearance.MouseOverBackColor = _isLightTheme
            ? Color.FromArgb(238, 240, 244)
            : Color.FromArgb(64, 64, 72);
    }

    private void ApplyWindowAttributes()
    {
        int dark = _isLightTheme ? 0 : 1;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref dark,
            sizeof(int));

        int round = NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref round,
            sizeof(int));
    }

    private void RebuildLayout()
    {
        SuspendLayout();

        int pad = S(16);
        int width = S(364);
        int contentWidth = width - (pad * 2);

        _titleLabel.Font = _titleFont;
        _titleLabel.Location = new Point(pad, pad);

        _refreshButton.Font = _buttonFont;
        _refreshButton.Size = new Size(S(98), S(28));
        _refreshButton.Location = new Point(width - pad - _refreshButton.Width, pad - S(2));

        _cardsHost.SuspendLayout();
        while (_cardsHost.Controls.Count > 0)
        {
            Control old = _cardsHost.Controls[0];
            _cardsHost.Controls.RemoveAt(0);
            old.Dispose();
        }

        int cardY = 0;
        int cardGap = S(10);
        if (_devices.Count == 0)
        {
            var emptyPanel = new EmptyDeviceCard(
                _cardColor,
                _cardBorderColor,
                _textPrimary,
                _textSecondary)
            {
                Location = new Point(0, 0),
                Size = new Size(contentWidth, S(74))
            };
            _cardsHost.Controls.Add(emptyPanel);
            cardY = emptyPanel.Height;
        }
        else
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                DeviceBatteryInfo dev = _devices[i];
                bool isVisible = _settings.IsDeviceVisible(dev);
                var card = new DeviceCardControl(
                    dev,
                    isVisible,
                    visible => DeviceVisibilityChanged?.Invoke(this, (dev, visible)),
                    _isLightTheme,
                    _cardColor,
                    _cardBorderColor,
                    _textPrimary,
                    _textSecondary,
                    _trackColor,
                    _linkFont)
                {
                    Location = new Point(0, cardY),
                    Size = new Size(contentWidth, S(94))
                };
                _cardsHost.Controls.Add(card);
                cardY += card.Height + (i < _devices.Count - 1 ? cardGap : 0);
            }
        }

        _cardsHost.Location = new Point(pad, pad + S(38));
        _cardsHost.Size = new Size(contentWidth, cardY);
        _cardsHost.ResumeLayout();

        int y = _cardsHost.Bottom + S(12);
        _divider.Location = new Point(pad, y);
        _divider.Size = new Size(contentWidth, 1);
        y += S(10);

        _modeLabel.Font = _linkFont;
        _modeLabel.Location = new Point(pad, y);
        y += S(20);

        int gap = S(6);
        int colW = (contentWidth - gap) / 2;
        int col2W = contentWidth - colW - gap;
        int btnH = S(28);

        _modeSplitButton.Font = _buttonFont;
        _modeSplitButton.Location = new Point(pad, y);
        _modeSplitButton.Size = new Size(colW, btnH);

        _modeNumbersButton.Font = _buttonFont;
        _modeNumbersButton.Location = new Point(pad + colW + gap, y);
        _modeNumbersButton.Size = new Size(col2W, btnH);

        y += btnH + gap;

        _modeCyclingButton.Font = _buttonFont;
        _modeCyclingButton.Location = new Point(pad, y);
        _modeCyclingButton.Size = new Size(colW, btnH);

        _modeCombinedButton.Font = _buttonFont;
        _modeCombinedButton.Location = new Point(pad + colW + gap, y);
        _modeCombinedButton.Size = new Size(col2W, btnH);

        StyleModeButtons();
        y += btnH + S(10);

        _suppressEvents = true;
        try
        {
            _autostartCheckBox.Font = _bodyFont;
            _autostartCheckBox.Checked = _autostartEnabled;
            _autostartCheckBox.Location = new Point(pad, y);
            y += S(26);

            _alertCheckBox.Font = _bodyFont;
            _alertCheckBox.Checked = _settings.LowBatteryAlertEnabled;
            _alertCheckBox.Text = $"Akku-Warnung unter {_settings.LowBatteryThreshold} %";
            _alertCheckBox.Location = new Point(pad, y);
            y += S(30);
        }
        finally
        {
            _suppressEvents = false;
        }

        _taskbarLink.Font = _linkFont;
        _taskbarLink.Location = new Point(pad, y + S(5));

        _exitButton.Font = _buttonFont;
        _exitButton.Size = new Size(S(84), S(28));
        _exitButton.Location = new Point(width - pad - _exitButton.Width, y);

        ClientSize = new Size(width, _exitButton.Bottom + pad);
        ResumeLayout(true);
        Invalidate();
    }

    private int S(int logicalPx) => (int)Math.Round(logicalPx * (Math.Max(96, DeviceDpi) / 96.0));

    private sealed class DeviceCardControl : Control
    {
        private readonly DeviceBatteryInfo _device;
        private readonly bool _isLight;
        private readonly Color _cardColor;
        private readonly Color _borderColor;
        private readonly Color _textPrimary;
        private readonly Color _textSecondary;
        private readonly Color _trackColor;
        private readonly CheckBox _visibleCheckBox;

        public DeviceCardControl(
            DeviceBatteryInfo device,
            bool isVisibleInTray,
            Action<bool> onVisibilityChanged,
            bool isLight,
            Color cardColor,
            Color borderColor,
            Color textPrimary,
            Color textSecondary,
            Color trackColor,
            Font checkBoxFont)
        {
            _device = device;
            _isLight = isLight;
            _cardColor = cardColor;
            _borderColor = borderColor;
            _textPrimary = textPrimary;
            _textSecondary = textSecondary;
            _trackColor = trackColor;
            DoubleBuffered = true;

            float scale = Math.Max(96, DeviceDpi) / 96f;
            _visibleCheckBox = new CheckBox
            {
                Text = "Im Tray anzeigen",
                Checked = isVisibleInTray,
                AutoSize = true,
                BackColor = cardColor,
                ForeColor = textSecondary,
                Font = checkBoxFont,
                Cursor = Cursors.Hand,
                TabStop = false,
                Location = new Point((int)Math.Round(12f * scale), (int)Math.Round(67f * scale))
            };
            _visibleCheckBox.CheckedChanged += (_, _) => onVisibilityChanged(_visibleCheckBox.Checked);
            Controls.Add(_visibleCheckBox);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = Math.Max(96, DeviceDpi) / 96f;
            float r = 8f * scale;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            using (GraphicsPath cardPath = TrayIconRenderer.CreateRoundedRect(bounds, r))
            using (var bg = new SolidBrush(_cardColor))
            using (var border = new Pen(_borderColor, 1f))
            {
                g.FillPath(bg, cardPath);
                g.DrawPath(border, cardPath);
            }

            float pad = 12f * scale;
            DrawCardGlyph(g, _device.Kind, new RectangleF(pad, 13f * scale, 26f * scale, 26f * scale), _textPrimary);

            string pctText = _device.IsConnected && _device.BatteryPercent is int pct
                ? $"{pct} %"
                : "--";
            Color accentColor = GetAccentColor();

            using var pctFont = new Font("Segoe UI", 14f, FontStyle.Bold);
            using var nameFont = new Font("Segoe UI", 9.75f, FontStyle.Bold);
            using var statusFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);

            SizeF pctSize = g.MeasureString(pctText, pctFont);
            float pctX = Width - pad - pctSize.Width;
            bool highlightPct = _device.IsCharging ||
                                _device.IsChargingComplete ||
                                (_device.IsConnected && _device.BatteryPercent <= 20);
            using (var pctBrush = new SolidBrush(highlightPct ? accentColor : _textPrimary))
            {
                g.DrawString(pctText, pctFont, pctBrush, pctX, 9f * scale);
            }

            float rightLimitX = pctX;
            if (_device.IsCharging)
            {
                float boltW = 10f * scale;
                float boltH = 14f * scale;
                float boltX = pctX - boltW - 2f * scale;
                float boltY = 14f * scale;
                TrayIconRenderer.DrawVectorBolt(g, new RectangleF(boltX, boltY, boltW, boltH), accentColor);
                rightLimitX = boltX;
            }

            float textLeft = pad + 36f * scale;
            float textMaxW = Math.Max(40f, rightLimitX - textLeft - 6f * scale);
            using (var nameBrush = new SolidBrush(_textPrimary))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(_device.Name, nameFont, nameBrush, new RectangleF(textLeft, 10f * scale, textMaxW, 20f * scale), sf);
            }

            string estimatePart = !string.IsNullOrEmpty(_device.EstimatedTimeText)
                ? $" · {_device.EstimatedTimeText}"
                : string.Empty;
            string voltagePart = _device.BatteryMillivolts is int mv && mv > 0
                ? $" · {mv} mV ({mv / 1000.0:0.00} V)"
                : string.Empty;
            string timePart = voltagePart.Length == 0 && _device.LastUpdated is DateTime dt
                ? $" · Stand {dt:HH:mm}"
                : string.Empty;
            string statusBase = ChargingStatusText.ToDetailedStatus(_device.ChargingStatus, _device.IsConnected);
            Color statusColor = _device.IsCharging || _device.IsChargingComplete ? accentColor : _textSecondary;
            using (var statusBrush = new SolidBrush(statusColor))
            using (var statusSf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float statusMaxW = Math.Max(60f, Width - textLeft - pad);
                g.DrawString(
                    statusBase + estimatePart + voltagePart + timePart,
                    statusFont,
                    statusBrush,
                    new RectangleF(textLeft, 29f * scale, statusMaxW, 18f * scale),
                    statusSf);
            }

            float barY = Height - 38f * scale;
            float barH = 6f * scale;
            float barW = Width - (pad * 2);
            using (GraphicsPath trackPath = TrayIconRenderer.CreateRoundedRect(new RectangleF(pad, barY, barW, barH), barH / 2f))
            using (var trackBrush = new SolidBrush(_trackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (_device.IsConnected && _device.BatteryPercent is int fillPct && fillPct > 0)
            {
                float fillW = Math.Clamp(barW * (fillPct / 100f), barH, barW);
                using GraphicsPath fillPath = TrayIconRenderer.CreateRoundedRect(new RectangleF(pad, barY, fillW, barH), barH / 2f);
                using var fillBrush = new SolidBrush(accentColor);
                g.FillPath(fillBrush, fillPath);
            }
        }

        private Color GetAccentColor()
        {
            if (!_device.IsConnected || _device.BatteryPercent is null)
            {
                return _textSecondary;
            }

            if (_device.IsCharging || _device.IsChargingComplete)
            {
                return _isLight ? Color.FromArgb(21, 128, 61) : Color.FromArgb(34, 197, 94);
            }

            return _device.BatteryPercent.Value switch
            {
                <= 15 => _isLight ? Color.FromArgb(220, 38, 38) : Color.FromArgb(239, 68, 68),
                <= 30 => _isLight ? Color.FromArgb(194, 65, 12) : Color.FromArgb(245, 158, 11),
                _ => _isLight ? Color.FromArgb(21, 128, 61) : Color.FromArgb(34, 197, 94)
            };
        }

        private static void DrawCardGlyph(Graphics g, DeviceKind kind, RectangleF rect, Color color)
        {
            float stroke = Math.Max(1.5f, rect.Width * 0.07f);
            using var pen = new Pen(color, stroke);
            using var brush = new SolidBrush(color);

            if (kind == DeviceKind.Keyboard)
            {
                var kbRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.18f, rect.Width, rect.Height * 0.64f);
                using GraphicsPath path = TrayIconRenderer.CreateRoundedRect(kbRect, 3f);
                g.DrawPath(pen, path);

                float keySize = kbRect.Width * 0.1f;
                for (int i = 0; i < 4; i++)
                {
                    float kx = kbRect.X + kbRect.Width * (0.17f + i * 0.19f);
                    g.FillRectangle(brush, kx, kbRect.Y + kbRect.Height * 0.28f, keySize, keySize);
                }

                g.FillRectangle(
                    brush,
                    kbRect.X + kbRect.Width * 0.25f,
                    kbRect.Bottom - kbRect.Height * 0.30f,
                    kbRect.Width * 0.50f,
                    Math.Max(1.5f, keySize * 0.9f));
            }
            else if (kind == DeviceKind.Mouse)
            {
                float mw = rect.Width * 0.68f;
                var mRect = new RectangleF(rect.X + (rect.Width - mw) / 2f, rect.Y + 1f, mw, rect.Height - 2f);
                using GraphicsPath path = TrayIconRenderer.CreateRoundedRect(mRect, mw * 0.48f);
                g.DrawPath(pen, path);
                g.DrawLine(
                    pen,
                    mRect.X + mRect.Width / 2f,
                    mRect.Y + 1f,
                    mRect.X + mRect.Width / 2f,
                    mRect.Y + mRect.Height * 0.42f);
            }
            else if (kind == DeviceKind.Headset)
            {
                float padX = rect.Width * 0.12f;
                var bandRect = new RectangleF(rect.X + padX, rect.Y + rect.Height * 0.10f, rect.Width - padX * 2f, rect.Height * 0.66f);
                g.DrawArc(pen, bandRect, 180f, 180f);

                float cupW = rect.Width * 0.22f;
                float cupH = rect.Height * 0.44f;
                float cupY = rect.Y + rect.Height * 0.44f;
                using GraphicsPath leftCup = TrayIconRenderer.CreateRoundedRect(new RectangleF(rect.X + padX * 0.5f, cupY, cupW, cupH), 2.5f);
                using GraphicsPath rightCup = TrayIconRenderer.CreateRoundedRect(new RectangleF(rect.Right - padX * 0.5f - cupW, cupY, cupW, cupH), 2.5f);
                g.FillPath(brush, leftCup);
                g.FillPath(brush, rightCup);
            }
            else
            {
                using GraphicsPath path = TrayIconRenderer.CreateRoundedRect(rect, 4f);
                g.DrawPath(pen, path);
            }
        }
    }

    private sealed class EmptyDeviceCard : Control
    {
        private readonly Color _cardColor;
        private readonly Color _borderColor;
        private readonly Color _textPrimary;
        private readonly Color _textSecondary;

        public EmptyDeviceCard(Color cardColor, Color borderColor, Color textPrimary, Color textSecondary)
        {
            _cardColor = cardColor;
            _borderColor = borderColor;
            _textPrimary = textPrimary;
            _textSecondary = textSecondary;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = Math.Max(96, DeviceDpi) / 96f;
            using (GraphicsPath path = TrayIconRenderer.CreateRoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8f * scale))
            using (var bg = new SolidBrush(_cardColor))
            using (var border = new Pen(_borderColor, 1f))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            using var b1 = new SolidBrush(_textPrimary);
            using var b2 = new SolidBrush(_textSecondary);

            g.DrawString("Keine Logitech-Bluetooth-Geräte gefunden", titleFont, b1, 12f * scale, 14f * scale);
            g.DrawString("Stelle sicher, dass Tastatur und Maus per Bluetooth gekoppelt sind.", subFont, b2, 12f * scale, 36f * scale);
        }
    }
}
