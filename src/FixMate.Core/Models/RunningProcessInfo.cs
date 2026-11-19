namespace FixMate.Core.Models;

public sealed class RunningProcessInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long WorkingSetBytes { get; init; }
    public double? CpuUsagePercent { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
}
