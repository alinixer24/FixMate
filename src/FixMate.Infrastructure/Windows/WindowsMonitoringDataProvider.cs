using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using FixMate.Core.Results;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsMonitoringDataProvider : IMonitoringDataProvider
{
    private readonly object _sampleLock = new();
    private CpuTimes? _previousCpuTimes;
    private NetworkTotals? _previousNetworkTotals;

    public Task<MonitoringSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Collect(cancellationToken), cancellationToken);
    }

    private MonitoringSnapshot Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpu = ReadCpuUsage();
        cancellationToken.ThrowIfCancellationRequested();
        var memory = ReadMemoryUsage();
        cancellationToken.ThrowIfCancellationRequested();
        var disk = ReadDiskActivity();
        cancellationToken.ThrowIfCancellationRequested();
        var network = ReadNetworkActivity();

        return new MonitoringSnapshot
        {
            CpuUsagePercent = cpu,
            MemoryUsagePercent = memory.Usage,
            MemoryUsedBytes = memory.UsedBytes,
            MemoryTotalBytes = memory.TotalBytes,
            DiskReadBytesPerSecond = disk.Read,
            DiskWriteBytesPerSecond = disk.Write,
            NetworkReceiveBytesPerSecond = network.Receive,
            NetworkSendBytesPerSecond = network.Send,
            CollectedAt = DateTimeOffset.Now
        };
    }

    private MetricReading ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return Unavailable("Windows could not read processor times.");
        }

        var current = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
        lock (_sampleLock)
        {
            var previous = _previousCpuTimes;
            _previousCpuTimes = current;
            if (previous is null)
            {
                return Unavailable("Collecting the first processor sample.");
            }

            if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
            {
                return Unavailable("Processor counters reset during this interval; collecting a new baseline.");
            }

            var idleDelta = current.Idle - previous.Idle;
            var totalDelta = (current.Kernel - previous.Kernel) + (current.User - previous.User);
            if (totalDelta == 0 || totalDelta < idleDelta)
            {
                return Unavailable("Processor usage is not available for this interval.");
            }

            var percent = (totalDelta - idleDelta) * 100d / totalDelta;
            return Available(Math.Clamp(percent, 0, 100));
        }
    }

    private static MemoryReading ReadMemoryUsage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return new MemoryReading(Unavailable("Windows could not read physical memory status."), null, null);
        }

        var used = status.TotalPhysical - status.AvailablePhysical;
        var usagePercent = used * 100d / status.TotalPhysical;
        return new MemoryReading(
            Available(Math.Clamp(usagePercent, 0, 100), $"{FormatBytes(used)} used of {FormatBytes(status.TotalPhysical)}"),
            (long)used,
            (long)status.TotalPhysical);
    }

    private static DiskReading ReadDiskActivity()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (!string.Equals(
                            Convert.ToString(item["Name"], CultureInfo.InvariantCulture),
                            "_Total",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var readValue = item["DiskReadBytesPersec"];
                    var writeValue = item["DiskWriteBytesPersec"];
                    if (readValue is null || writeValue is null)
                    {
                        var incompleteSample = Unavailable("Windows returned an incomplete disk activity sample.");
                        return new DiskReading(incompleteSample, incompleteSample);
                    }

                    return new DiskReading(
                        Available(Convert.ToDouble(readValue, CultureInfo.InvariantCulture)),
                        Available(Convert.ToDouble(writeValue, CultureInfo.InvariantCulture)));
                }
            }

            var unavailable = Unavailable("Windows did not return aggregate disk activity counters.");
            return new DiskReading(unavailable, unavailable);
        }
        catch (Exception exception) when (IsMonitoringException(exception))
        {
            var unavailable = Unavailable($"Disk activity is unavailable: {exception.Message}");
            return new DiskReading(unavailable, unavailable);
        }
    }

    private MetricReadingPair ReadNetworkActivity()
    {
        try
        {
            long received = 0;
            long sent = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var statistics = adapter.GetIPv4Statistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }

            var current = new NetworkTotals(received, sent, System.Diagnostics.Stopwatch.GetTimestamp());
            lock (_sampleLock)
            {
                var previous = _previousNetworkTotals;
                _previousNetworkTotals = current;
                if (previous is null)
                {
                    var warming = Unavailable("Collecting the first network sample.");
                    return new MetricReadingPair(warming, warming);
                }

                var elapsedSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(previous.Timestamp, current.Timestamp).TotalSeconds;
                if (elapsedSeconds <= 0)
                {
                    var unavailable = Unavailable("Network activity is unavailable for this interval.");
                    return new MetricReadingPair(unavailable, unavailable);
                }

                return new MetricReadingPair(
                    Available(Math.Max(0, current.Received - previous.Received) / elapsedSeconds),
                    Available(Math.Max(0, current.Sent - previous.Sent) / elapsedSeconds));
            }
        }
        catch (NetworkInformationException exception)
        {
            var unavailable = Unavailable($"Network activity is unavailable: {exception.Message}");
            return new MetricReadingPair(unavailable, unavailable);
        }
    }

    private static MetricReading Available(double value, string details = "") => new(value, details);

    private static MetricReading Unavailable(string details) => new(null, details);

    private static bool IsMonitoringException(Exception exception)
    {
        return exception is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException or FormatException or OverflowException;
    }

    private static string FormatBytes(ulong bytes)
    {
        return $"{bytes / 1024d / 1024d / 1024d:N1} GB";
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
    {
        return ((ulong)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record CpuTimes(ulong Idle, ulong Kernel, ulong User);
    private sealed record NetworkTotals(long Received, long Sent, long Timestamp);
    private sealed record MemoryReading(MetricReading Usage, long? UsedBytes, long? TotalBytes);
    private sealed record DiskReading(MetricReading Read, MetricReading Write);
    private sealed record MetricReadingPair(MetricReading Receive, MetricReading Send);
}
