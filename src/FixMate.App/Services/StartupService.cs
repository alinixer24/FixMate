using Microsoft.Win32;

namespace FixMate.App.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FixMate";

    public void Apply(bool launchAtStartup, bool startMinimized)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The current user's Windows startup settings could not be opened.");

        if (!launchAtStartup)
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("FixMate could not determine its executable path for Windows startup.");
        }

        var command = $"\"{executablePath}\"{(startMinimized ? " --minimized" : string.Empty)}";
        runKey.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
