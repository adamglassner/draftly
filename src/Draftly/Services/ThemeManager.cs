using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Draftly.Services;

/// <summary>
/// Builds the colour palette in code and swaps it into Application.Resources.
/// Every control uses DynamicResource, so switching is instant.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static readonly (string Name, Color Light, Color Dark)[] Accents =
    {
        ("Indigo",   C("#4F5BD5"), C("#8C96F7")),
        ("Ocean",    C("#0B6FB8"), C("#5EB3F0")),
        ("Teal",     C("#0E7C7E"), C("#4CC7C9")),
        ("Forest",   C("#2E7D32"), C("#7CCB80")),
        ("Rose",     C("#B5245E"), C("#F07AA6")),
        ("Amber",    C("#A35A00"), C("#FFB55C")),
        ("Graphite", C("#4A5568"), C("#A9B4C6")),
    };

    public static event Action ThemeChanged;

    private static ResourceDictionary _current;

    public static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch { }
        return false;
    }

    public static void Apply(AppSettings s)
    {
        IsDark = s.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDark(),
        };

        var accent = Accents.FirstOrDefault(a => a.Name == s.Accent);
        if (accent.Name == null) accent = Accents[0];
        var acc = IsDark ? accent.Dark : accent.Light;

        var d = new ResourceDictionary();
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); d[key] = b; d[key + "Color"] = c; }

        if (!IsDark)
        {
            B("WindowBg", C("#F3F3F3"));
            B("ChromeBg", C("#F3F3F3"));
            B("RibbonBg", C("#FBFBFB"));
            B("RibbonBorder", C("#E3E3E3"));
            B("Text", C("#1B1B1B"));
            B("TextSecondary", C("#5C5C5C"));
            B("TextDisabled", C("#A0A0A0"));
            B("ControlBg", C("#00FFFFFF"));
            B("ControlHoverBg", C("#0F000000"));
            B("ControlPressedBg", C("#1A000000"));
            B("ControlBorder", C("#D6D6D6"));
            B("InputBg", C("#FFFFFF"));
            B("InputBorder", C("#C8C8C8"));
            B("ButtonBg", C("#FFFFFF"));
            B("ButtonHoverBg", C("#F6F6F6"));
            B("CanvasBg", C("#E6E7EB"));
            B("PageShadow", C("#33000000"));
            B("RulerBg", C("#FFFFFF"));
            B("RulerMargin", C("#E2E3E7"));
            B("RulerText", C("#5C5C5C"));
            B("StatusBg", C("#F3F3F3"));
            B("Divider", C("#E0E0E0"));
            B("MenuBg", C("#FCFCFC"));
            B("MenuHover", C("#EDEDED"));
            B("ToolTipBg", C("#FAFAFA"));
            B("ScrollThumb", C("#8A8A8A"));
            B("ScrollThumbHover", C("#5E5E5E"));
            B("DialogBg", C("#F9F9F9"));
            B("CardBg", C("#FFFFFF"));
            B("DangerText", C("#C42B1C"));
        }
        else
        {
            B("WindowBg", C("#202020"));
            B("ChromeBg", C("#202020"));
            B("RibbonBg", C("#2B2B2B"));
            B("RibbonBorder", C("#3A3A3A"));
            B("Text", C("#F2F2F2"));
            B("TextSecondary", C("#BDBDBD"));
            B("TextDisabled", C("#6E6E6E"));
            B("ControlBg", C("#00FFFFFF"));
            B("ControlHoverBg", C("#18FFFFFF"));
            B("ControlPressedBg", C("#26FFFFFF"));
            B("ControlBorder", C("#4A4A4A"));
            B("InputBg", C("#1C1C1C"));
            B("InputBorder", C("#555555"));
            B("ButtonBg", C("#373737"));
            B("ButtonHoverBg", C("#424242"));
            B("CanvasBg", C("#161616"));
            B("PageShadow", C("#66000000"));
            B("RulerBg", C("#2B2B2B"));
            B("RulerMargin", C("#202020"));
            B("RulerText", C("#C8C8C8"));
            B("StatusBg", C("#202020"));
            B("Divider", C("#3A3A3A"));
            B("MenuBg", C("#2C2C2C"));
            B("MenuHover", C("#3D3D3D"));
            B("ToolTipBg", C("#2C2C2C"));
            B("ScrollThumb", C("#8A8A8A"));
            B("ScrollThumbHover", C("#BBBBBB"));
            B("DialogBg", C("#262626"));
            B("CardBg", C("#2E2E2E"));
            B("DangerText", C("#FF99A4"));
        }

        // The document page: dark in dark mode (unless the user prefers a white page).
        bool darkPage = IsDark && s.DarkPageInDarkMode;
        B("PageBg", darkPage ? C("#1F1F1F") : C("#FFFFFF"));
        B("PageText", darkPage ? C("#E8E8E8") : C("#000000"));
        B("PageBorder", darkPage ? C("#383838") : C("#D9D9D9"));
        IsDarkPage = darkPage;

        B("Accent", acc);
        B("AccentHover", Blend(acc, IsDark ? Colors.White : Colors.Black, 0.12));
        B("AccentText", IsDark ? C("#111111") : Colors.White);
        B("AccentSoft", Color.FromArgb((byte)(IsDark ? 0x40 : 0x26), acc.R, acc.G, acc.B));
        B("AccentSoftHover", Color.FromArgb((byte)(IsDark ? 0x55 : 0x38), acc.R, acc.G, acc.B));
        B("Selection", Color.FromArgb(0x66, acc.R, acc.G, acc.B));
        B("FocusRing", acc);

        var app = Application.Current;
        if (_current != null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Insert(0, d);
        _current = d;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
    }

    public static bool IsDarkPage { get; private set; }

    // ---------- native dark title bar ----------
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+ / Win11); 19 on older Win10 builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        // Nudge Windows 10 to repaint the non-client area.
        if (w.IsVisible && Environment.OSVersion.Version.Build < 22000)
        {
            var s = w.WindowState;
            if (s == WindowState.Normal) { w.Width += 1; w.Width -= 1; }
        }
    }

    /// <summary>Call from each window so it picks up the title bar colour once it has a handle.</summary>
    public static void Attach(Window w)
    {
        w.SourceInitialized += (_, _) => ApplyTitleBar(w);
    }

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
