namespace FixMate.Core.Models;

public sealed class OperatingSystemInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DetectionStatus Status { get; set; } = DetectionStatus.Unavailable;
    public string Details { get; set; } = string.Empty;
}
