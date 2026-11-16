namespace FixMate.Core.Models;

public sealed class HardwareComponent
{
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DetectionStatus Status { get; set; } = DetectionStatus.Unavailable;
    public string Details { get; set; } = string.Empty;
}
