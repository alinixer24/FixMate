namespace FixMate.App.ViewModels;

public sealed record MonitoringSample(
    DateTimeOffset CollectedAt,
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    double? NetworkReceiveBytesPerSecond,
    double? NetworkSendBytesPerSecond);
