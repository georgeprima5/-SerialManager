using System.Globalization;
using Microsoft.Win32;
using SerialManager.Data;

namespace SerialManager.Services;

public enum ThemeMode
{
    Light,
    Dark,
    System,   // 跟隨 Windows
}

public record FontOption(string Key, string DisplayName, string CssFamily);

public record SizeOption(string Key, string DisplayName, int Pixels);

/// <summary>
/// 一組畫面用的顏色。畫面（CSS）和視窗外框（MainForm）都從這裡取色。
/// </summary>
public record Palette(
    string Bg, string Surface, string SurfaceAlt, string Border, string WindowBorder,
    string Text, string TextMuted, string SegmentBg, string SegmentActive, string Hover, string Accent)
{
    public string ToCssVariables() =>
        $"--bg:{Bg};--surface:{Surface};--surface-alt:{SurfaceAlt};--border:{Border};" +
        $"--text:{Text};--text-muted:{TextMuted};--segment-bg:{SegmentBg};" +
        $"--segment-active:{SegmentActive};--hover:{Hover};--accent:{Accent};";
}

/// <summary>
/// 外觀設定（主題、自訂顏色、字型、字體大小）：從 Settings 表讀取，修改時存回去並通知畫面更新。
/// </summary>
public class AppearanceService
{
    public static readonly IReadOnlyList<FontOption> Fonts =
    [
        new("jhenghei", "微軟正黑體", "'Microsoft JhengHei', sans-serif"),
        new("pmingliu", "新細明體", "PMingLiU, serif"),
        new("kaiu", "標楷體", "DFKai-SB, serif"),
        new("noto", "思源黑體（Noto Sans TC）", "'Noto Sans TC', sans-serif"),
        new("yahei", "微軟雅黑", "'Microsoft YaHei', sans-serif"),
    ];

    public static readonly IReadOnlyList<SizeOption> Sizes =
    [
        new("small", "小", 13),
        new("medium", "中", 14),
        new("large", "大", 16),
    ];

    // 預設色相（藍灰色）與「明暗」可調整的範圍
    private const int DefaultLightHue = 213;
    private const int DefaultDarkHue = 219;
    private const int LightShiftMin = -10, LightShiftMax = 4;
    private const int DarkShiftMin = -8, DarkShiftMax = 10;

    private const string ThemeKey = "Appearance.Theme";
    private const string FontKey = "Appearance.Font";
    private const string SizeKey = "Appearance.FontSize";
    private const string LightHueKey = "Appearance.LightHue";
    private const string LightShiftKey = "Appearance.LightShift";
    private const string DarkHueKey = "Appearance.DarkHue";
    private const string DarkShiftKey = "Appearance.DarkShift";

    private readonly SettingsRepository _settings;

    public ThemeMode Theme { get; private set; }
    public FontOption Font { get; private set; }
    public SizeOption Size { get; private set; }

    // 淺色、深色各自的色相（0～360）與明暗偏移
    private int _lightHue, _lightShift, _darkHue, _darkShift;

    /// <summary>
    /// 任何外觀設定改變時觸發（包含「跟隨 Windows」模式下 Windows 切換深淺色）。
    /// </summary>
    public event Action? Changed;

    public AppearanceService(SettingsRepository settings)
    {
        _settings = settings;
        Font = Fonts[0];
        Size = Sizes[1];
        Load();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 實際要用深色顯示嗎？「跟隨 Windows」時讀取 Windows 的應用程式深淺色設定。
    /// </summary>
    public bool IsDark => Theme switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => WindowsUsesDarkTheme(),
    };

    /// <summary>
    /// 目前畫面使用的那一組顏色（淺色或深色）。
    /// </summary>
    public Palette Palette => IsDark ? DarkPalette(_darkHue, _darkShift) : LightPalette(_lightHue, _lightShift);

    // 滑桿調整的是「目前正在顯示的」那一組（淺色或深色）
    public int CurrentHue => IsDark ? _darkHue : _lightHue;
    public int CurrentShift => IsDark ? _darkShift : _lightShift;
    public int ShiftMin => IsDark ? DarkShiftMin : LightShiftMin;
    public int ShiftMax => IsDark ? DarkShiftMax : LightShiftMax;

    public void SetTheme(ThemeMode theme)
    {
        Theme = theme;
        _settings.Set(ThemeKey, theme.ToString());
        Changed?.Invoke();
    }

    public void SetFont(string? key)
    {
        Font = Fonts.FirstOrDefault(f => f.Key == key) ?? Font;
        _settings.Set(FontKey, Font.Key);
        Changed?.Invoke();
    }

    public void SetSize(string key)
    {
        Size = Sizes.FirstOrDefault(s => s.Key == key) ?? Size;
        _settings.Set(SizeKey, Size.Key);
        Changed?.Invoke();
    }

    /// <summary>
    /// 拖動滑桿時即時預覽，還不存檔。
    /// </summary>
    public void PreviewColor(int hue, int shift)
    {
        hue = Math.Clamp(hue, 0, 360);
        shift = Math.Clamp(shift, ShiftMin, ShiftMax);
        if (IsDark)
        {
            (_darkHue, _darkShift) = (hue, shift);
        }
        else
        {
            (_lightHue, _lightShift) = (hue, shift);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// 放開滑桿時存檔。
    /// </summary>
    public void SaveColor()
    {
        if (IsDark)
        {
            _settings.Set(DarkHueKey, _darkHue.ToString(CultureInfo.InvariantCulture));
            _settings.Set(DarkShiftKey, _darkShift.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            _settings.Set(LightHueKey, _lightHue.ToString(CultureInfo.InvariantCulture));
            _settings.Set(LightShiftKey, _lightShift.ToString(CultureInfo.InvariantCulture));
        }
    }

    public int DefaultHue => IsDark ? DefaultDarkHue : DefaultLightHue;

    /// <summary>
    /// 色相恢復預設（只影響目前這一組：淺色或深色）。
    /// </summary>
    public void ResetHue()
    {
        PreviewColor(DefaultHue, CurrentShift);
        SaveColor();
    }

    /// <summary>
    /// 明暗恢復預設（只影響目前這一組：淺色或深色）。
    /// </summary>
    public void ResetShift()
    {
        PreviewColor(CurrentHue, 0);
        SaveColor();
    }

    /// <summary>
    /// 重新從資料庫讀取設定（讀取備份後使用）。
    /// </summary>
    public void Reload()
    {
        Load();
        Changed?.Invoke();
    }

    private void Load()
    {
        Theme = Enum.TryParse<ThemeMode>(_settings.Get(ThemeKey), out var theme) ? theme : ThemeMode.System;
        Font = Fonts.FirstOrDefault(f => f.Key == _settings.Get(FontKey)) ?? Fonts[0];
        Size = Sizes.FirstOrDefault(s => s.Key == _settings.Get(SizeKey)) ?? Sizes[1];
        _lightHue = GetInt(LightHueKey, DefaultLightHue);
        _lightShift = GetInt(LightShiftKey, 0);
        _darkHue = GetInt(DarkHueKey, DefaultDarkHue);
        _darkShift = GetInt(DarkShiftKey, 0);
    }

    private int GetInt(string key, int defaultValue) =>
        int.TryParse(_settings.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    // 淺色：背景亮度約 94%，明暗偏移會讓背景、卡片、框線一起變亮或變暗
    private static Palette LightPalette(int h, int s) => new(
        Bg: Hsl(h, 52, 94 + s),
        Surface: Hsl(h, 75, 98 + s),
        SurfaceAlt: Hsl(h, 45, 95 + s),
        Border: Hsl(h, 34, 88 + s),
        WindowBorder: Hsl(h, 35, 83 + s),
        Text: Hsl(h, 28, 17),
        TextMuted: Hsl(h, 10, 47),
        SegmentBg: Hsl(h, 35, 91 + s),
        SegmentActive: Hsl(h, 75, 100),
        Hover: "rgba(0, 0, 0, 0.06)",
        Accent: Hsl(h, 67, 51));

    // 深色：背景亮度約 14%
    private static Palette DarkPalette(int h, int s) => new(
        Bg: Hsl(h, 15, 14 + s),
        Surface: Hsl(h, 15, 18 + s),
        SurfaceAlt: Hsl(h, 15, 22 + s),
        Border: Hsl(h, 14, 26 + s),
        WindowBorder: Hsl(h, 14, 26 + s),
        Text: Hsl(h, 7, 91),
        TextMuted: Hsl(h, 10, 64),
        SegmentBg: Hsl(h, 16, 12 + s),
        SegmentActive: Hsl(h, 15, 27 + s),
        Hover: "rgba(255, 255, 255, 0.08)",
        Accent: Hsl(h, 100, 71));

    /// <summary>
    /// HSL（色相 0～360、飽和度 0～100、亮度 0～100）轉成 #RRGGBB。
    /// </summary>
    private static string Hsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 100) / 100;
        l = Math.Clamp(l, 0, 100) / 100;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return $"#{ToByte(r + m):X2}{ToByte(g + m):X2}{ToByte(b + m):X2}";
    }

    private static int ToByte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Theme == ThemeMode.System && e.Category == UserPreferenceCategory.General)
        {
            Changed?.Invoke();
        }
    }

    // Windows 設定 → 個人化 → 色彩 → 預設應用程式模式；值 0 代表深色
    private static bool WindowsUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
