using FixMate.Core.Models;

namespace FixMate.Core.Results;

public sealed class MonitoringSnapshot
{
    public MetricReading CpuUsagePercent { get; init; } = new(null, "Waiting for a sample.");
    public MetricReading MemoryUsagePercent { get; init; } = new(null, "Waiting for a sample.");
    public MetricReading DiskReadBytesPerSecond { get; init; } = new(null, "Waiting for a sample.");
    public MetricReading DiskWriteBytesPerSecond { get; init; } = new(null, "Waiting for a sample.");
    public MetricReading NetworkReceiveBytesPerSecond { get; init; } = new(null, "Waiting for a sample.");
    public MetricReading NetworkSendBytesPerSecond { get; init; } = new(null, "Waiting for a sample.");
    public long? MemoryUsedBytes { get; init; }
    public long? MemoryTotalBytes { get; init; }
    public DateTimeOffset CollectedAt { get; init; }
}
