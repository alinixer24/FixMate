using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using FixMate.Core.Models;

namespace FixMate.App.Services;

public sealed class ThemeService : IDisposable
{
    private AppSettings? _currentSettings;
    private static readonly IReadOnlyDictionary<string, Color> AccentColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        ["#4D8DFF"] = Color.FromRgb(0x4D, 0x8D, 0xFF),
        ["#9B7CFF"] = Color.FromRgb(0x9B, 0x7C, 0xFF),
        ["#36C6B3"] = Color.FromRgb(0x36, 0xC6, 0xB3),
        ["#EA6C91"] = Color.FromRgb(0xEA, 0x6C, 0x91)
    };

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _currentSettings = settings;
        ApplyPalette(settings);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _currentSettings = null;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentSettings?.ThemeMode != "System")
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            var settings = _currentSettings;
            if (settings?.ThemeMode != "System")
            {
                return;
            }

            try
            {
                ApplyPalette(settings);
            }
            catch (Exception exception)
            {
                Trace.TraceError($"FixMate could not refresh the system theme: {exception}");
            }
        }));
    }

    private static void ApplyPalette(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        var light = IsLightTheme(settings.ThemeMode);
        var accent = AccentColors[settings.AccentColor];
        var palette = light
            ? new Dictionary<string, Color>
            {
                ["WindowBackgroundBrush"] = Color.FromRgb(0xF4, 0xF6, 0xFA),
                ["SidebarBackgroundBrush"] = Colors.White,
                ["SurfaceBrush"] = Colors.White,
                ["ElevatedSurfaceBrush"] = Color.FromRgb(0xED, 0xF1, 0xF7),
                ["BorderBrush"] = Color.FromRgb(0xD8, 0xDE, 0xE8),
                ["PrimaryTextBrush"] = Color.FromRgb(0x1B, 0x24, 0x32),
                ["SecondaryTextBrush"] = Color.FromRgb(0x5B, 0x67, 0x78)
            }
            : new Dictionary<string, Color>
            {
                ["WindowBackgroundBrush"] = Color.FromRgb(0x11, 0x13, 0x18),
                ["SidebarBackgroundBrush"] = Color.FromRgb(0x18, 0x1B, 0x22),
                ["SurfaceBrush"] = Color.FromRgb(0x20, 0x24, 0x2D),
                ["ElevatedSurfaceBrush"] = Color.FromRgb(0x29, 0x2E, 0x39),
                ["BorderBrush"] = Color.FromRgb(0x34, 0x3A, 0x46),
                ["PrimaryTextBrush"] = Color.FromRgb(0xF1, 0xF3, 0xF7),
                ["SecondaryTextBrush"] = Color.FromRgb(0x9C, 0xA5, 0xB4)
            };

        foreach (var (key, color) in palette)
        {
            resources[key] = new SolidColorBrush(color);
        }

        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x8F));
        resources["WarningBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x4B));
        resources["ErrorBrush"] = new SolidColorBrush(Color.FromRgb(0xEF, 0x62, 0x62));
        resources["SelectedNavigationBrush"] = new SolidColorBrush(Color.FromArgb(light ? (byte)0x26 : (byte)0x36, accent.R, accent.G, accent.B));
        resources["ButtonPadding"] = settings.CompactLayout ? new Thickness(8, 5, 8, 5) : new Thickness(12, 7, 12, 7);
        resources["NavigationButtonPadding"] = settings.CompactLayout ? new Thickness(6, 5, 6, 5) : new Thickness(8, 9, 8, 9);
        resources["NavigationButtonMargin"] = settings.CompactLayout ? new Thickness(0, 1, 0, 1) : new Thickness(0, 2, 0, 2);
    }

    private static bool IsLightTheme(string themeMode)
    {
        if (themeMode == "Light")
        {
            return true;
        }

        if (themeMode != "System")
        {
            return false;
        }

        var value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            null);
        return value is int lightTheme && lightTheme != 0;
    }
}
