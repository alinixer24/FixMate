namespace FixMate.Core.Models;

public sealed record StartupEntry(
    string Id,
    string Name,
    string Command,
    string Source,
    bool IsEnabled,
    bool CanChange)
{
    public string ActionText => IsEnabled ? "Disable" : "Enable";
    public string StateText => IsEnabled ? "Enabled at sign-in" : "Disabled at sign-in";
}
