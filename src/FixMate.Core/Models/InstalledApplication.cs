namespace FixMate.Core.Models;

public sealed record InstalledApplication(
    string Name,
    string Version,
    string Publisher,
    DateTime? InstallDate,
    string RegistryView);
