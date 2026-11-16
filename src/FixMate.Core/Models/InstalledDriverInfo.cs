namespace FixMate.Core.Models;

public sealed class InstalledDriverInfo
{
    public string DeviceName { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string DriverProvider { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public DateTimeOffset? DriverDate { get; init; }
    public string DeviceClass { get; init; } = string.Empty;
    public string InfName { get; init; } = string.Empty;
    public bool IsSigned { get; init; }
}
