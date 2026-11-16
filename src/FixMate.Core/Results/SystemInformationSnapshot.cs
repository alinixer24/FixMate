using FixMate.Core.Models;

namespace FixMate.Core.Results;

public sealed class SystemInformationSnapshot
{
    public OperatingSystemInfo OperatingSystem { get; init; } = new();
    public IReadOnlyList<HardwareComponent> Components { get; init; } = [];
    public DateTimeOffset CollectedAt { get; init; }
}
