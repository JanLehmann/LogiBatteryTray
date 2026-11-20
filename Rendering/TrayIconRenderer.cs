using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LogiBatteryTray.Models;
using LogiBatteryTray.Services;

namespace LogiBatteryTray.Rendering;

/// <summary>
/// Renders crisp, theme-aware, DPI-exact system tray icons and diagnostic preview sheets.
/// </summary>
public static class TrayIconRenderer
{
    public static Icon RenderDeviceIcon(DeviceBatteryInfo device, int size, bool isLightTaskbar)
    {
        using Bitmap bmp = RenderDeviceBitmap(device, size, isLightTaskbar);
        return CreateIconFromBitmap(bmp);
    }

    public static Icon RenderDeviceNumbersOnlyIcon(DeviceBatteryInfo device, int size, bool isLightTaskbar)
    {
        using Bitmap bmp = RenderDeviceNumbersOnlyBitmap(device, size, isLightTaskbar);
        return CreateIconFromBitmap(bmp);
    }

    public static Icon RenderCombinedIcon(IReadOnlyList<DeviceBatteryInfo> devices, int size, bool isLightTaskbar)
    {
        using Bitmap bmp = RenderCombinedBitmap(devices, size, isLightTaskbar);
        return CreateIconFromBitmap(bmp);
    }

    public static Icon RenderEmptyIcon(int size, bool isLightTaskbar)
    {
        var placeholder = new DeviceBatteryInfo(
            "none",
            "Keine Logitech-Geräte",
            DeviceKind.Other,
            null,
            false,
            null,
            null);
        return RenderDeviceIcon(placeholder, size, isLightTaskbar);
    }

    public static Bitmap RenderDeviceBitmap(DeviceBatteryInfo device, int size, bool isLightTaskbar)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        Palette palette = GetPalette(isLightTaskbar);

        string text = FormatPercentText(device);
        Color textColor = GetTextColor(device, palette);
        Color glyphColor = device.IsConnected ? palette.SubtleGlyph : palette.Offline;
        Color barColor = GetBatteryBarColor(device, palette);

        if (size <= 18)
        {
            RenderDevice16(bmp, device, text, glyphColor, textColor, palette.Track, barColor, palette.Good);
        }
        else if (size <= 22)
        {
            RenderDevice20(bmp, device, text, glyphColor, textColor, palette.Track, barColor, palette.Good);
        }
        else
        {
            RenderDeviceHighDpi(bmp, device, text, glyphColor, textColor, palette.Track, barColor, palette.Good);
        }

        return bmp;
    }

    public static Bitmap RenderDeviceNumbersOnlyBitmap(DeviceBatteryInfo device, int size, bool isLightTaskbar)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        Palette palette = GetPalette(isLightTaskbar);

        string text = FormatPercentText(device);
        Color textColor = GetTextColor(device, palette);
        bool showUnderline = device.IsCharging;

        if (size <= 18)
        {
            PixelFont.DrawCenteredLarge16(bmp, text, textColor, showUnderline ? 1 : null);
            if (showUnderline)
            {
                DrawPixelBar(bmp, 2, 14, 12, 1, 100, palette.Good, palette.Good);
            }
        }
        else if (size <= 22)
        {
            PixelFont.DrawCenteredLarge20(bmp, text, textColor, showUnderline ? 2 : null);
            if (showUnderline)
            {
                DrawPixelBar(bmp, 2, 18, 16, 2, 100, palette.Good, palette.Good);
            }
        }
        else
        {
            RenderDeviceNumbersOnlyHighDpi(bmp, text, textColor, showUnderline ? palette.Good : null);
        }

        return bmp;
    }

    public static Bitmap RenderCombinedBitmap(IReadOnlyList<DeviceBatteryInfo> devices, int size, bool isLightTaskbar)
    {
        if (devices.Count == 0)
        {
            return RenderDeviceBitmap(
                new DeviceBatteryInfo("none", "Logitech", DeviceKind.Other, null, false, null, null),
                size,
                isLightTaskbar);
        }

        if (devices.Count == 1)
        {
            return RenderDeviceBitmap(devices[0], size, isLightTaskbar);
        }

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        Palette palette = GetPalette(isLightTaskbar);
        DeviceBatteryInfo top = devices[0];
        DeviceBatteryInfo bottom = devices[1];

        if (size <= 18)
        {
            RenderCombined16(bmp, top, bottom, palette);
        }
        else if (size <= 22)
        {
            RenderCombined20(bmp, top, bottom, palette);
        }
        else
        {
            RenderCombinedHighDpi(bmp, top, bottom, palette);
        }

        return bmp;
    }

    public static string ExportPreviewSheet(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "preview_sheet.png");

        var now = DateTime.Now;
        DeviceBatteryInfo[] splitSamples =
        [
            new("kb70", "MX KEYS S", DeviceKind.Keyboard, 70, true, now, null, ChargingStatus.Discharging),
            new("kb70chg", "MX KEYS S", DeviceKind.Keyboard, 70, true, now, null, ChargingStatus.Recharging),
            new("kb100", "MX KEYS S", DeviceKind.Keyboard, 100, true, now, null, ChargingStatus.ChargingComplete),
            new("kb15", "MX KEYS S", DeviceKind.Keyboard, 15, true, now, null, ChargingStatus.Discharging),
            new("kboff", "MX KEYS S", DeviceKind.Keyboard, 70, false, now, null, ChargingStatus.Unknown),
            new("ms95", "MX Master 3S", DeviceKind.Mouse, 95, true, now, null, ChargingStatus.Discharging),
            new("ms25chg", "MX Master 3S", DeviceKind.Mouse, 25, true, now, null, ChargingStatus.Recharging),
            new("hs52", "PRO X Wireless Gaming Headset", DeviceKind.Headset, 52, true, now, null, ChargingStatus.Discharging),
            new("hs52chg", "PRO X Wireless Gaming Headset", DeviceKind.Headset, 52, true, now, null, ChargingStatus.Recharging),
            new("ms05", "MX Master 3S", DeviceKind.Mouse, 5, true, now, null, ChargingStatus.Discharging),
            new("msoff", "MX Master 3S", DeviceKind.Mouse, null, false, null, null, ChargingStatus.Unknown)
        ];

        DeviceBatteryInfo[][] combinedSamples =
        [
            [splitSamples[0], splitSamples[5]],
            [splitSamples[1], splitSamples[9]]
        ];

        DeviceBatteryInfo[] numbersOnlySamples =
        [
            splitSamples[0],
            splitSamples[1],
            splitSamples[2],
            splitSamples[3],
            splitSamples[5],
            splitSamples[6],
            splitSamples[9],
            splitSamples[10]
        ];

        int[] sizes = [16, 20, 24, 32];
        const int scale = 4;
        const int colGap = 12;
        const int rowPadding = 16;
        int sampleCount = splitSamples.Length + combinedSamples.Length + numbersOnlySamples.Length;

        int maxIconScaled = 32 * scale;
        int cellWidth = maxIconScaled + colGap;
        int sheetWidth = 110 + (sampleCount * cellWidth) + 20;
        int stripHeight = sizes.Length * (maxIconScaled + 20) + rowPadding;
        int sheetHeight = stripHeight * 2 + 30;

        using var sheet = new Bitmap(sheetWidth, sheetHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(sheet);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var labelFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        for (int themeIdx = 0; themeIdx < 2; themeIdx++)
        {
            bool isLight = themeIdx == 0;
            int stripY = themeIdx * (stripHeight + 16);
            Color bgColor = isLight ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);
            Color fgColor = isLight ? Color.FromArgb(24, 24, 27) : Color.White;

            using var bgBrush = new SolidBrush(bgColor);
            using var fgBrush = new SolidBrush(fgColor);
            g.FillRectangle(bgBrush, 0, stripY, sheetWidth, stripHeight);

            string themeLabel = isLight ? "LIGHT TASKBAR" : "DARK TASKBAR";
            g.DrawString(themeLabel, labelFont, fgBrush, 10, stripY + 6);

            for (int sizeIdx = 0; sizeIdx < sizes.Length; sizeIdx++)
            {
                int size = sizes[sizeIdx];
                int rowY = stripY + 28 + sizeIdx * (maxIconScaled + 16);
                g.DrawString($"{size}px (4x)", labelFont, fgBrush, 10, rowY + (size * scale) / 3);

                for (int sIdx = 0; sIdx < sampleCount; sIdx++)
                {
                    using Bitmap iconBmp = sIdx < splitSamples.Length
                        ? RenderDeviceBitmap(splitSamples[sIdx], size, isLight)
                        : sIdx < splitSamples.Length + combinedSamples.Length
                            ? RenderCombinedBitmap(combinedSamples[sIdx - splitSamples.Length], size, isLight)
                            : RenderDeviceNumbersOnlyBitmap(
                                numbersOnlySamples[sIdx - splitSamples.Length - combinedSamples.Length],
                                size,
                                isLight);

                    int drawX = 110 + sIdx * cellWidth;
                    g.DrawImage(iconBmp, new Rectangle(drawX, rowY, size * scale, size * scale));

                    using var borderPen = new Pen(isLight ? Color.FromArgb(50, 0, 0, 0) : Color.FromArgb(50, 255, 255, 255));
                    g.DrawRectangle(borderPen, drawX - 1, rowY - 1, size * scale + 1, size * scale + 1);
                }
            }
        }

        sheet.Save(outputPath, ImageFormat.Png);
        return outputPath;
    }

    private static void RenderDevice20(
        Bitmap bmp,
        DeviceBatteryInfo device,
        string text,
        Color glyphColor,
        Color textColor,
        Color trackColor,
        Color barColor,
        Color boltColor)
    {
        bool charging = device.IsCharging;
        switch (device.Kind)
        {
            case DeviceKind.Keyboard:
                int kbX = charging ? 1 : (20 - 13) / 2;
                PixelFont.DrawGlyph(bmp, PixelFont.Keyboard13x5, kbX, 1, glyphColor);
                break;
            case DeviceKind.Mouse:
                PixelFont.DrawGlyph(bmp, PixelFont.Mouse7x6, (20 - 7) / 2, 0, glyphColor);
                break;
            case DeviceKind.Headset:
                PixelFont.DrawGlyph(bmp, PixelFont.Headset9x6, (20 - 9) / 2, 0, glyphColor);
                break;
            default:
                PixelFont.DrawGlyph(bmp, PixelFont.Other7x5, (20 - 7) / 2, 1, glyphColor);
                break;
        }

        if (charging)
        {
            PixelFont.DrawGlyph(bmp, PixelFont.Bolt4x6, 16, 0, boltColor);
        }

        PixelFont.DrawCenteredText8(bmp, text, 7, textColor);
        DrawPixelBar(bmp, 1, 16, 18, 3, device.IsConnected ? device.BatteryPercent : null, trackColor, barColor);
    }

    private static void RenderDevice16(
        Bitmap bmp,
        DeviceBatteryInfo device,
        string text,
        Color glyphColor,
        Color textColor,
        Color trackColor,
        Color barColor,
        Color boltColor)
    {
        bool charging = device.IsCharging;
        switch (device.Kind)
        {
            case DeviceKind.Keyboard:
                int kbX = charging ? 1 : (16 - 11) / 2;
                PixelFont.DrawGlyph(bmp, PixelFont.Keyboard11x5, kbX, 0, glyphColor);
                break;
            case DeviceKind.Mouse:
                PixelFont.DrawGlyph(bmp, PixelFont.Mouse5x5, (16 - 5) / 2, 0, glyphColor);
                break;
            case DeviceKind.Headset:
                PixelFont.DrawGlyph(bmp, PixelFont.Headset7x5, (16 - 7) / 2, 0, glyphColor);
                break;
            default:
                PixelFont.DrawGlyph(bmp, PixelFont.Other7x5, (16 - 7) / 2, 0, glyphColor);
                break;
        }

        if (charging)
        {
            PixelFont.DrawGlyph(bmp, PixelFont.Bolt3x5, 13, 0, boltColor);
        }

        PixelFont.DrawCenteredText7(bmp, text, 6, textColor);
        DrawPixelBar(bmp, 1, 14, 14, 2, device.IsConnected ? device.BatteryPercent : null, trackColor, barColor);
    }

    private static void RenderDeviceHighDpi(
        Bitmap bmp,
        DeviceBatteryInfo device,
        string text,
        Color glyphColor,
        Color textColor,
        Color trackColor,
        Color barColor,
        Color boltColor)
    {
        int size = bmp.Width;
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        bool charging = device.IsCharging;
        float glyphX = charging && device.Kind == DeviceKind.Keyboard ? size * 0.10f : size * 0.22f;
        float glyphW = charging && device.Kind == DeviceKind.Keyboard ? size * 0.54f : size * 0.56f;
        DrawVectorDeviceGlyph(g, device.Kind, new RectangleF(glyphX, size * 0.03f, glyphW, size * 0.25f), glyphColor);

        if (charging)
        {
            DrawVectorBolt(g, new RectangleF(size * 0.72f, size * 0.02f, size * 0.24f, size * 0.28f), boltColor);
        }

        float fontSize = text.Length >= 3 ? size * 0.36f : size * 0.43f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var textRect = new RectangleF(-1f, size * 0.27f, size + 2f, size * 0.55f);
        g.DrawString(text, font, brush, textRect, sf);

        int barX = Math.Max(1, (int)Math.Round(size * 0.06f));
        int barW = size - (barX * 2);
        int barH = Math.Max(2, (int)Math.Round(size * 0.14f));
        int barY = size - barH - Math.Max(1, size / 24);
        DrawRoundedBar(g, barX, barY, barW, barH, device.IsConnected ? device.BatteryPercent : null, trackColor, barColor);
    }

    private static void RenderDeviceNumbersOnlyHighDpi(Bitmap bmp, string text, Color textColor, Color? underlineColor)
    {
        int size = bmp.Width;
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(textColor);
        using var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.Alignment = StringAlignment.Center;
        sf.LineAlignment = StringAlignment.Center;
        sf.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

        float fontSize = text.Length >= 3 ? size * 0.56f : size * 0.74f;
        if (underlineColor.HasValue)
        {
            fontSize *= 0.92f;
        }

        Font? chosenFont = null;
        try
        {
            while (fontSize > 8f)
            {
                chosenFont?.Dispose();
                chosenFont = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                SizeF measured = g.MeasureString(text, chosenFont, size * 2, sf);
                if (measured.Width <= size - 1f)
                {
                    break;
                }

                fontSize -= 1.5f;
            }

            float rectHeight = underlineColor.HasValue ? size * 0.85f : size;
            g.DrawString(text, chosenFont!, brush, new RectangleF(0f, 0f, size, rectHeight), sf);
        }
        finally
        {
            chosenFont?.Dispose();
        }

        if (underlineColor is Color uColor)
        {
            int barX = Math.Max(2, (int)Math.Round(size * 0.10f));
            int barW = size - (barX * 2);
            int barH = Math.Max(2, (int)Math.Round(size * 0.09f));
            int barY = size - barH - Math.Max(1, size / 24);
            DrawRoundedBar(g, barX, barY, barW, barH, 100, uColor, uColor);
        }
    }

    private static void RenderCombined20(
        Bitmap bmp,
        DeviceBatteryInfo top,
        DeviceBatteryInfo bottom,
        Palette palette)
    {
        RenderCombinedRow20(bmp, top, rowY: 0, barY: 7, palette);
        RenderCombinedRow20(bmp, bottom, rowY: 10, barY: 17, palette);
    }

    private static void RenderCombinedRow20(
        Bitmap bmp,
        DeviceBatteryInfo device,
        int rowY,
        int barY,
        Palette palette)
    {
        Color glyphColor = device.IsConnected ? palette.SubtleGlyph : palette.Offline;
        string[] miniGlyph = device.Kind switch
        {
            DeviceKind.Keyboard => PixelFont.MiniKeyboard5x5,
            DeviceKind.Mouse => PixelFont.MiniMouse5x6,
            DeviceKind.Headset => PixelFont.MiniHeadset5x5,
            _ => PixelFont.MiniKeyboard5x5
        };

        int glyphY = device.Kind == DeviceKind.Mouse ? rowY : rowY + 1;
        PixelFont.DrawGlyph(bmp, miniGlyph, 0, glyphY, glyphColor);

        string text = FormatPercentText(device);
        int textWidth = PixelFont.MeasureText6(text);
        int textX = Math.Max(6, 19 - textWidth);
        PixelFont.DrawText6(bmp, text, textX, rowY, GetTextColor(device, palette));

        DrawPixelBar(
            bmp,
            0,
            barY,
            20,
            2,
            device.IsConnected ? device.BatteryPercent : null,
            palette.Track,
            GetBatteryBarColor(device, palette));
    }

    private static void RenderCombined16(
        Bitmap bmp,
        DeviceBatteryInfo top,
        DeviceBatteryInfo bottom,
        Palette palette)
    {
        RenderCombinedRow16(bmp, top, rowY: 0, barY: 6, palette);
        RenderCombinedRow16(bmp, bottom, rowY: 8, barY: 14, palette);
    }

    private static void RenderCombinedRow16(
        Bitmap bmp,
        DeviceBatteryInfo device,
        int rowY,
        int barY,
        Palette palette)
    {
        Color glyphColor = device.IsConnected ? palette.SubtleGlyph : palette.Offline;
        string[] miniGlyph = device.Kind switch
        {
            DeviceKind.Mouse => PixelFont.MiniMouse4x5,
            DeviceKind.Headset => PixelFont.MiniHeadset5x4,
            _ => PixelFont.MiniKeyboard4x5
        };

        int glyphY = device.Kind == DeviceKind.Keyboard ? rowY + 1 : rowY;
        PixelFont.DrawGlyph(bmp, miniGlyph, 0, glyphY, glyphColor);

        string text = FormatPercentText(device);
        int textWidth = PixelFont.MeasureText5(text);
        int textX = Math.Max(6, 16 - textWidth);
        PixelFont.DrawText5(bmp, text, textX, rowY, GetTextColor(device, palette));

        DrawPixelBar(
            bmp,
            0,
            barY,
            16,
            1,
            device.IsConnected ? device.BatteryPercent : null,
            palette.Track,
            GetBatteryBarColor(device, palette));
    }

    private static void RenderCombinedHighDpi(
        Bitmap bmp,
        DeviceBatteryInfo top,
        DeviceBatteryInfo bottom,
        Palette palette)
    {
        int size = bmp.Width;
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        DrawCombinedRowHighDpi(g, top, 0, size / 2f, size, palette);
        DrawCombinedRowHighDpi(g, bottom, size / 2f, size / 2f, size, palette);
    }

    private static void DrawCombinedRowHighDpi(
        Graphics g,
        DeviceBatteryInfo device,
        float topY,
        float rowHeight,
        int totalWidth,
        Palette palette)
    {
        Color glyphColor = device.IsConnected ? palette.SubtleGlyph : palette.Offline;
        float glyphH = device.Kind == DeviceKind.Keyboard ? rowHeight * 0.48f : rowHeight * 0.58f;
        float glyphY = topY + (rowHeight * 0.72f - glyphH) / 2f;
        var glyphRect = new RectangleF(1f, glyphY, totalWidth * 0.28f, glyphH);
        DrawVectorDeviceGlyph(g, device.Kind, glyphRect, glyphColor);

        string text = FormatPercentText(device);
        float fontSize = text.Length >= 3 ? rowHeight * 0.62f : rowHeight * 0.68f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(GetTextColor(device, palette));
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var textRect = new RectangleF(totalWidth * 0.24f, topY - 1f, totalWidth * 0.76f, rowHeight * 0.76f);
        g.DrawString(text, font, brush, textRect, sf);

        int barH = Math.Max(2, (int)Math.Round(rowHeight * 0.18f));
        int barY = (int)Math.Round(topY + rowHeight - barH - 1f);
        DrawRoundedBar(
            g,
            1,
            barY,
            totalWidth - 2,
            barH,
            device.IsConnected ? device.BatteryPercent : null,
            palette.Track,
            GetBatteryBarColor(device, palette));
    }

    private static void DrawVectorDeviceGlyph(Graphics g, DeviceKind kind, RectangleF bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.3f, bounds.Height * 0.16f));
        using var fill = new SolidBrush(color);

        if (kind == DeviceKind.Keyboard)
        {
            var kbRect = new RectangleF(bounds.X, bounds.Y + bounds.Height * 0.14f, bounds.Width, bounds.Height * 0.70f);
            using GraphicsPath path = CreateRoundedRect(kbRect, Math.Max(1.2f, kbRect.Height * 0.2f));
            g.DrawPath(pen, path);

            float spaceW = kbRect.Width * 0.48f;
            float spaceH = Math.Max(1.3f, kbRect.Height * 0.2f);
            g.FillRectangle(
                fill,
                kbRect.X + (kbRect.Width - spaceW) / 2f,
                kbRect.Bottom - kbRect.Height * 0.36f,
                spaceW,
                spaceH);
        }
        else if (kind == DeviceKind.Mouse)
        {
            float w = Math.Min(bounds.Width, bounds.Height * 0.78f);
            var mouseRect = new RectangleF(bounds.X + (bounds.Width - w) / 2f, bounds.Y, w, bounds.Height);
            using GraphicsPath path = CreateRoundedRect(mouseRect, w * 0.45f);
            g.DrawPath(pen, path);
            g.DrawLine(
                pen,
                mouseRect.X + mouseRect.Width / 2f,
                mouseRect.Y + 1f,
                mouseRect.X + mouseRect.Width / 2f,
                mouseRect.Y + mouseRect.Height * 0.44f);
        }
        else if (kind == DeviceKind.Headset)
        {
            float hsW = Math.Min(bounds.Width, bounds.Height * 1.45f);
            float hsX = bounds.X + (bounds.Width - hsW) / 2f;
            float archH = bounds.Height * 0.80f;
            var bandRect = new RectangleF(hsX + pen.Width * 0.4f, bounds.Y + pen.Width * 0.4f, hsW - pen.Width * 0.8f, archH);
            g.DrawArc(pen, bandRect, 180f, 180f);

            float cupW = Math.Max(1.8f, hsW * 0.24f);
            float cupH = Math.Max(2.2f, bounds.Height * 0.54f);
            float cupY = bounds.Bottom - cupH;
            using GraphicsPath leftCup = CreateRoundedRect(new RectangleF(hsX, cupY, cupW, cupH), 1.2f);
            using GraphicsPath rightCup = CreateRoundedRect(new RectangleF(hsX + hsW - cupW, cupY, cupW, cupH), 1.2f);
            g.FillPath(fill, leftCup);
            g.FillPath(fill, rightCup);
        }
        else
        {
            using GraphicsPath path = CreateRoundedRect(bounds, 2f);
            g.DrawPath(pen, path);
        }
    }

    private static void DrawPixelBar(
        Bitmap bmp,
        int x,
        int y,
        int width,
        int height,
        int? percent,
        Color trackColor,
        Color barColor)
    {
        int fillWidth = percent is int pct && pct > 0
            ? Math.Clamp((int)Math.Round(width * (pct / 100.0)), 1, width)
            : 0;

        for (int dy = 0; dy < height; dy++)
        {
            int py = y + dy;
            if (py < 0 || py >= bmp.Height)
            {
                continue;
            }

            for (int dx = 0; dx < width; dx++)
            {
                int px = x + dx;
                if (px < 0 || px >= bmp.Width)
                {
                    continue;
                }

                bmp.SetPixel(px, py, dx < fillWidth ? barColor : trackColor);
            }
        }
    }

    private static void DrawRoundedBar(
        Graphics g,
        int x,
        int y,
        int width,
        int height,
        int? percent,
        Color trackColor,
        Color barColor)
    {
        float radius = height / 2f;
        using var trackBrush = new SolidBrush(trackColor);
        using GraphicsPath trackPath = CreateRoundedRect(new RectangleF(x, y, width, height), radius);
        g.FillPath(trackBrush, trackPath);

        if (percent is int pct && pct > 0)
        {
            float fillW = Math.Clamp(width * (pct / 100f), height, width);
            using var barBrush = new SolidBrush(barColor);
            using GraphicsPath barPath = CreateRoundedRect(new RectangleF(x, y, fillW, height), radius);
            g.FillPath(barBrush, barPath);
        }
    }

    internal static void DrawVectorBolt(Graphics g, RectangleF bounds, Color color)
    {
        PointF[] pts =
        [
            new(bounds.X + bounds.Width * 0.58f, bounds.Y),
            new(bounds.X + bounds.Width * 0.14f, bounds.Y + bounds.Height * 0.54f),
            new(bounds.X + bounds.Width * 0.48f, bounds.Y + bounds.Height * 0.54f),
            new(bounds.X + bounds.Width * 0.34f, bounds.Bottom),
            new(bounds.X + bounds.Width * 0.88f, bounds.Y + bounds.Height * 0.42f),
            new(bounds.X + bounds.Width * 0.52f, bounds.Y + bounds.Height * 0.42f)
        ];

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pts);
    }

    internal static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        if (d <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon CreateIconFromBitmap(Bitmap bmp)
    {
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static string FormatPercentText(DeviceBatteryInfo device)
    {
        return device.IsConnected && device.BatteryPercent is int pct
            ? pct.ToString()
            : "--";
    }

    private static Color GetTextColor(DeviceBatteryInfo device, Palette palette)
    {
        if (!device.IsConnected || device.BatteryPercent is null)
        {
            return palette.Offline;
        }

        if (device.IsCharging || device.IsChargingComplete)
        {
            return palette.Good;
        }

        return device.BatteryPercent.Value switch
        {
            <= 15 => palette.Low,
            <= 20 => palette.Warn,
            _ => palette.Foreground
        };
    }

    private static Color GetBatteryBarColor(DeviceBatteryInfo device, Palette palette)
    {
        if (!device.IsConnected || device.BatteryPercent is null)
        {
            return palette.Offline;
        }

        if (device.IsCharging || device.IsChargingComplete)
        {
            return palette.Good;
        }

        return device.BatteryPercent.Value switch
        {
            <= 15 => palette.Low,
            <= 30 => palette.Warn,
            _ => palette.Good
        };
    }

    private static Palette GetPalette(bool isLightTaskbar)
    {
        return isLightTaskbar
            ? new Palette(
                Foreground: Color.FromArgb(24, 24, 27),
                SubtleGlyph: Color.FromArgb(63, 63, 70),
                Track: Color.FromArgb(52, 0, 0, 0),
                Good: Color.FromArgb(21, 128, 61),
                Warn: Color.FromArgb(194, 65, 12),
                Low: Color.FromArgb(220, 38, 38),
                Offline: Color.FromArgb(113, 113, 122))
            : new Palette(
                Foreground: Color.White,
                SubtleGlyph: Color.FromArgb(212, 212, 216),
                Track: Color.FromArgb(68, 255, 255, 255),
                Good: Color.FromArgb(34, 197, 94),
                Warn: Color.FromArgb(245, 158, 11),
                Low: Color.FromArgb(239, 68, 68),
                Offline: Color.FromArgb(161, 161, 170));
    }

    private readonly record struct Palette(
        Color Foreground,
        Color SubtleGlyph,
        Color Track,
        Color Good,
        Color Warn,
        Color Low,
        Color Offline);
}
