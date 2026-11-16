using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsDiagnosticsProvider : IDiagnosticsProvider
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private readonly IMonitoringDataProvider _monitoringDataProvider;

    public WindowsDiagnosticsProvider(IMonitoringDataProvider monitoringDataProvider)
    {
        _monitoringDataProvider = monitoringDataProvider;
    }

    public Task<IReadOnlyList<DiagnosticResult>> RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DiagnosticResult>>(async () =>
        [
            await RunCheckAsync(DiagnosticArea.Cpu, CheckCpuAsync, cancellationToken),
            await RunCheckAsync(DiagnosticArea.Memory, CheckMemoryAsync, cancellationToken),
            await RunCheckAsync(DiagnosticArea.Network, CheckNetworkAsync, cancellationToken),
            await RunCheckAsync(DiagnosticArea.Storage, CheckStorageAsync, cancellationToken),
            await RunCheckAsync(DiagnosticArea.WindowsIntegrity, CheckWindowsIntegrityAsync, cancellationToken)
        ], cancellationToken);
    }

    public Task<string> RunRepairAsync(DiagnosticArea area, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () => area switch
        {
            DiagnosticArea.Network => await RunRepairCommandAsync(
                GetSystemExecutable("ipconfig.exe"), "/flushdns", elevated: false, cancellationToken),
            DiagnosticArea.Storage => await RepairStorageAsync(cancellationToken),
            DiagnosticArea.WindowsIntegrity => await RepairWindowsIntegrityAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown diagnostic area.")
        }, cancellationToken);
    }

    private static async Task<DiagnosticResult> RunCheckAsync(
        DiagnosticArea area,
        Func<CancellationToken, Task<DiagnosticCheckOutcome>> check,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            var outcome = await check(cancellationToken);
            return Result(area, outcome, startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result(area, new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "The check could not complete; no conclusion was reached.",
                $"{exception.GetType().Name}: {exception.Message}",
                "Review the technical details, then retry. If the problem persists, use the relevant Windows diagnostic tool."), startedAt);
        }
    }

    private async Task<DiagnosticCheckOutcome> CheckCpuAsync(CancellationToken cancellationToken)
    {
        await _monitoringDataProvider.GetSnapshotAsync(cancellationToken);
        var interval = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var snapshot = await _monitoringDataProvider.GetSnapshotAsync(cancellationToken);
        interval.Stop();
        var reading = snapshot.CpuUsagePercent;
        if (reading.Value is not double usage)
        {
            return new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "CPU utilization could not be measured during this sample.",
                $"Windows monitoring provider: {reading.Details} Sample interval: {interval.Elapsed.TotalMilliseconds:N0} ms.",
                "Retry the check. CPU utilization is a momentary measurement and does not by itself diagnose a processor fault.");
        }

        return new DiagnosticCheckOutcome(
            usage >= 90 ? DiagnosticStatus.Warning : DiagnosticStatus.Passed,
            $"CPU utilization was {usage:N1}% during an approximately one-second sample. A high sample may reflect current workload and does not establish a fault or root cause.",
            $"Measured utilization: {usage:N1}%. Sample collected: {snapshot.CollectedAt.ToLocalTime():G}. Sample interval: {interval.Elapsed.TotalMilliseconds:N0} ms. Source: Windows system processor-time counters.",
            usage >= 90
                ? "If high utilization persists while the PC is idle, check Task Manager for resource-heavy processes and compare with another sample. Do not treat a brief spike as evidence of a hardware problem."
                : "No high-CPU condition was observed in this brief sample. If the symptom is intermittent, repeat the check while it occurs and inspect the responsible process.",
            usage >= 90
                ? "A foreground task, background process, or scheduled work may explain a high sample. This brief measurement cannot identify which process or establish a CPU fault."
                : "This sample did not identify a CPU utilization issue. It does not rule out intermittent workload or a hardware fault.");
    }

    private async Task<DiagnosticCheckOutcome> CheckMemoryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _monitoringDataProvider.GetSnapshotAsync(cancellationToken);
        var reading = snapshot.MemoryUsagePercent;
        if (reading.Value is not double usage || snapshot.MemoryTotalBytes is not long totalBytes || totalBytes <= 0 ||
            snapshot.MemoryUsedBytes is not long usedBytes)
        {
            return new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "Physical memory usage could not be read.",
                $"Windows monitoring provider: {reading.Details}",
                "Retry the check. This usage check cannot determine whether a memory module is faulty.");
        }

        return new DiagnosticCheckOutcome(
            usage >= 90 ? DiagnosticStatus.Warning : DiagnosticStatus.Passed,
            $"Windows reported {FormatBytes(usedBytes)} used of {FormatBytes(totalBytes)} physical memory ({usage:N1}%) at the sample time. Memory usage alone cannot identify a faulty module.",
            $"Memory utilization: {usage:N1}%. Used: {usedBytes} bytes. Total: {totalBytes} bytes. Sample collected: {snapshot.CollectedAt.ToLocalTime():G}. Source: Windows GlobalMemoryStatusEx.",
            usage >= 90
                ? "If high usage persists, use Task Manager to review per-process memory consumption and close only applications you recognize. Consider Windows Memory Diagnostic for suspected memory errors; this usage check is not a hardware test."
                : "No high-memory-use condition was observed at the sample time. This does not test RAM for faults; use Windows Memory Diagnostic if memory errors are suspected.",
            usage >= 90
                ? "Multiple applications, a memory-heavy process, or Windows file caching may contribute to high usage. This snapshot cannot distinguish these causes or detect faulty RAM."
                : "This snapshot did not identify high memory usage. It does not rule out intermittent pressure or a hardware fault.");
    }

    private static async Task<DiagnosticCheckOutcome> CheckNetworkAsync(CancellationToken cancellationToken)
    {
        var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();
        if (activeAdapters.Length == 0)
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(adapter => $"{adapter.Name}: {adapter.OperationalStatus}");
            return new DiagnosticCheckOutcome(
                DiagnosticStatus.Failed,
                "No non-loopback network adapter is currently reported as active.",
                string.Join(Environment.NewLine, adapters.DefaultIfEmpty("Windows reported no non-loopback network adapters.")),
                "Check that Wi-Fi is enabled or the Ethernet cable is connected. This result does not identify why an adapter is inactive.",
                "An adapter may be disabled, disconnected, affected by a driver problem, or unavailable for another reason. This check cannot determine which explanation applies.");
        }

        var adapterDetails = activeAdapters.Select(adapter =>
        {
            var gateways = adapter.GetIPProperties().GatewayAddresses
                .Select(gateway => gateway.Address)
                .Where(address => !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any))
                .Select(address => address.ToString())
                .ToArray();
            return $"{adapter.Name}: gateway(s) {(gateways.Length == 0 ? "none" : string.Join(", ", gateways))}";
        }).ToArray();
        var hasGateway = activeAdapters.Any(adapter => adapter.GetIPProperties().GatewayAddresses
            .Any(gateway => !IPAddress.IsLoopback(gateway.Address) && !gateway.Address.Equals(IPAddress.Any)));
        if (!hasGateway)
        {
            return new DiagnosticCheckOutcome(
                DiagnosticStatus.Warning,
                $"Found {activeAdapters.Length} active adapter(s), but none reports a usable default gateway.",
                string.Join(Environment.NewLine, adapterDetails),
                "Check the router or network connection and the adapter's IP configuration. This check does not determine whether the network is intentionally isolated.",
                "The network may be intentionally isolated, or gateway configuration, DHCP, or router connectivity may be involved. The observed adapter data does not distinguish these cases.");
        }

        const string testHost = "www.microsoft.com";
        var addresses = await Dns.GetHostAddressesAsync(testHost, cancellationToken);
        var evidence = string.Join(Environment.NewLine, adapterDetails) + Environment.NewLine +
            $"DNS lookup for {testHost}: {(addresses.Length > 0 ? string.Join(", ", addresses.Select(address => address.ToString())) : "no addresses returned")}.";
        return addresses.Length > 0
            ? new DiagnosticCheckOutcome(
                DiagnosticStatus.Passed,
                $"DNS resolved {testHost}. This confirms name resolution only; it does not verify general internet reachability or application access.",
                evidence,
                "No follow-up is indicated by this DNS check alone. If an application still cannot connect, test that application's endpoint and review its proxy, VPN, and firewall settings.",
                "No DNS failure was observed for this hostname. This does not rule out application-specific, firewall, or other connectivity problems.")
            : new DiagnosticCheckOutcome(
                DiagnosticStatus.Warning,
                $"DNS returned no addresses for {testHost}.",
                evidence,
                "Check DNS configuration and retry. A VPN, proxy, firewall, filtering service, or temporary DNS issue may affect this lookup; this result does not distinguish among them.",
                "Resolver configuration or reachability, VPN/proxy behavior, filtering, or a temporary DNS issue may explain the failed lookup. The check cannot isolate the cause.");
    }

    private static Task<DiagnosticCheckOutcome> CheckStorageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fixedDrives = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => new
            {
                drive.Name,
                drive.TotalSize,
                drive.AvailableFreeSpace
            })
            .ToArray();

        if (fixedDrives.Length == 0)
        {
            return Task.FromResult(new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "Windows reported no ready fixed drives, so capacity and disk status could not be assessed.",
                "No ready fixed volumes were returned by DriveInfo.",
                "Check whether the expected drive is connected and accessible. Removable and unavailable volumes are not assessed by this check."));
        }

        var lowSpace = fixedDrives
            .Where(drive => drive.TotalSize > 0 && drive.AvailableFreeSpace / (double)drive.TotalSize < 0.1)
            .ToArray();
        var diskStatuses = ReadDiskStatuses()
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unknownStatus = diskStatuses.Length == 0 || diskStatuses.Any(status => status.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
        var reportedProblem = diskStatuses.Any(status => !status.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                                                          !status.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
        var driveDetails = fixedDrives.Select(drive =>
        {
            var percentFree = drive.TotalSize > 0 ? drive.AvailableFreeSpace / (double)drive.TotalSize * 100 : 0;
            return $"{drive.Name}: {drive.AvailableFreeSpace / (1024d * 1024 * 1024):N1} GB free of {drive.TotalSize / (1024d * 1024 * 1024):N1} GB ({percentFree:N1}% free)";
        });
        var details = string.Join(Environment.NewLine, driveDetails) + Environment.NewLine +
            $"Windows Win32_DiskDrive status: {(diskStatuses.Length == 0 ? "not reported" : string.Join(", ", diskStatuses))}." + Environment.NewLine +
            "This check does not read SMART attributes or establish physical drive health.";

        if (lowSpace.Length > 0)
        {
            return Task.FromResult(new DiagnosticCheckOutcome(
                DiagnosticStatus.Warning,
                $"Low free space (under 10%) was observed on: {string.Join(", ", lowSpace.Select(drive => drive.Name))}.",
                details,
                "Review the drive's storage breakdown and preview files before cleanup. This check does not select or delete files.",
                "The observed low space may be due to installed applications, personal files, caches, or other data. This check does not identify which files are safe to remove."));
        }

        if (reportedProblem)
        {
            return Task.FromResult(new DiagnosticCheckOutcome(
                DiagnosticStatus.Warning,
                $"Windows returned non-OK disk status value(s): {string.Join(", ", diskStatuses.Where(status => !status.Equals("OK", StringComparison.OrdinalIgnoreCase)))}. This needs further investigation and is not a confirmed hardware diagnosis.",
                details,
                "Back up important data if possible, then inspect the drive in Windows and consult its manufacturer's diagnostic utility. Windows status values are not a substitute for SMART or a full hardware test.",
                "The non-OK Windows status may reflect a device, driver, controller, or reporting issue. It is not by itself proof of physical drive failure."));
        }

        if (unknownStatus)
        {
            return Task.FromResult(new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "No low-space condition was observed, but Windows did not provide a usable status for every disk. Physical drive health was not established.",
                details,
                "Review the reported capacity information. Use a drive-specific health tool or an appropriate Windows disk scan if you need to investigate physical or file-system health."));
        }

        return Task.FromResult(new DiagnosticCheckOutcome(
            DiagnosticStatus.Passed,
            "No low-space condition was observed and Windows reported OK for the disks queried. This does not establish SMART or overall physical drive health.",
            details,
            "No follow-up is indicated by these checks alone. Use a drive-specific health tool if you need SMART attributes or a broader hardware assessment."));
    }

    private static Task<DiagnosticCheckOutcome> CheckWindowsIntegrityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAdministrator())
        {
            return Task.FromResult(new DiagnosticCheckOutcome(
                DiagnosticStatus.Inconclusive,
                "Windows integrity checks were not run because FixMate is not elevated.",
                "DISM /CheckHealth and SFC /verifyonly require administrator access in this workflow.",
                "If you want these checks, run FixMate as administrator or run the supported DISM and SFC verification commands yourself. Elevation is not required for the other checks."));
        }

        return CheckWindowsIntegrityAsAdministratorAsync(cancellationToken);
    }

    private static async Task<DiagnosticCheckOutcome> CheckWindowsIntegrityAsAdministratorAsync(
        CancellationToken cancellationToken)
    {
        var dism = await RunCapturedCommandAsync(
            GetSystemExecutable("dism.exe"), "/Online /Cleanup-Image /CheckHealth", cancellationToken);
        var sfc = await RunCapturedCommandAsync(
            GetSystemExecutable("sfc.exe"), "/verifyonly", cancellationToken);
        var details = $"DISM /Online /Cleanup-Image /CheckHealth (exit code {dism.ExitCode}):{Environment.NewLine}{LimitOutput(dism.Output)}{Environment.NewLine}{Environment.NewLine}" +
            $"SFC /verifyonly (exit code {sfc.ExitCode}):{Environment.NewLine}{LimitOutput(sfc.Output)}";
        const string nextSteps = "Review the command output above (long output is truncated). These verification commands do not make repairs; if output reports corruption, record it and choose a repair only after reviewing the Windows guidance.";

        if (dism.ExitCode != 0 || sfc.ExitCode != 0)
        {
            return new DiagnosticCheckOutcome(
                DiagnosticStatus.Failed,
                $"At least one Windows integrity command did not complete successfully (DISM exit code {dism.ExitCode}; SFC exit code {sfc.ExitCode}). The failure does not by itself identify the underlying cause.",
                details,
                nextSteps);
        }

        return new DiagnosticCheckOutcome(
            DiagnosticStatus.Inconclusive,
            "Both verification commands exited with code 0. Exit codes alone do not show whether corruption was found; review the reported output before drawing a conclusion.",
            details,
            nextSteps);
    }

    private static IReadOnlyList<string> ReadDiskStatuses()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Status FROM Win32_DiskDrive");
        using var results = searcher.Get();
        var statuses = new List<string>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                statuses.Add(Convert.ToString(item["Status"]) ?? string.Empty);
            }
        }

        return statuses;
    }

    private static async Task<string> RepairStorageAsync(CancellationToken cancellationToken)
    {
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.Name.TrimEnd('\\'))
            .ToArray();
        if (drives.Length == 0)
        {
            return "Failed: no ready fixed drives were found to scan.";
        }

        var results = new List<string>();
        foreach (var drive in drives)
        {
            var result = await RunRepairCommandAsync(
                GetSystemExecutable("chkdsk.exe"), $"{drive} /scan", elevated: true, cancellationToken);
            results.Add($"{drive}: {result}");
        }

        return string.Join(Environment.NewLine, results);
    }

    private static async Task<string> RepairWindowsIntegrityAsync(CancellationToken cancellationToken)
    {
        var dismResult = await RunRepairCommandAsync(
            GetSystemExecutable("dism.exe"), "/Online /Cleanup-Image /RestoreHealth", elevated: true, cancellationToken);
        if (dismResult != "Completed successfully (exit code 0).")
        {
            return $"DISM: {dismResult} SFC was not started.";
        }

        var sfcResult = await RunRepairCommandAsync(
            GetSystemExecutable("sfc.exe"), "/scannow", elevated: true, cancellationToken);
        return $"DISM: {dismResult} SFC: {sfcResult}";
    }

    private static async Task<string> RunRepairCommandAsync(
        string executable,
        string arguments,
        bool elevated,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = elevated,
                Verb = elevated ? "runas" : string.Empty,
                CreateNoWindow = !elevated,
                WindowStyle = elevated ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !elevated,
                RedirectStandardError = !elevated
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }

        if (!elevated)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            await WaitForProcessAsync(process, timeout.Token);
            var standardOutput = await output;
            var standardError = await error;
            if (process.ExitCode != 0)
            {
                return $"Failed with exit code {process.ExitCode}: {FirstNonEmptyLine(standardError, standardOutput)}";
            }

            return "Completed successfully (exit code 0). " + FirstNonEmptyLine(standardOutput, standardError);
        }

        using var elevatedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        elevatedTimeout.CancelAfter(CommandTimeout);
        await WaitForProcessAsync(process, elevatedTimeout.Token);
        return process.ExitCode == 0
            ? "Completed successfully (exit code 0)."
            : $"Failed with exit code {process.ExitCode}.";
    }

    private static async Task<(int ExitCode, string Output)> RunCapturedCommandAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        await WaitForProcessAsync(process, timeout.Token);
        return (process.ExitCode, $"{await standardOutput} {await standardError}".Trim());
    }

    private static async Task WaitForProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static DiagnosticResult Result(DiagnosticArea area, DiagnosticCheckOutcome outcome, DateTimeOffset startedAt)
    {
        return new DiagnosticResult
        {
            Area = area,
            TestName = GetTestName(area),
            Status = outcome.Status,
            Summary = outcome.Summary,
            PossibleCauses = outcome.PossibleCauses,
            TechnicalDetails = outcome.TechnicalDetails,
            RecommendedNextSteps = outcome.RecommendedNextSteps,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.Now
        };
    }

    private static string GetTestName(DiagnosticArea area) => area switch
    {
        DiagnosticArea.Cpu => "CPU utilization",
        DiagnosticArea.Memory => "Physical memory usage",
        DiagnosticArea.Network => "Network and DNS",
        DiagnosticArea.Storage => "Storage capacity and Windows status",
        DiagnosticArea.WindowsIntegrity => "Windows integrity verification",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown diagnostic area.")
    };

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:N1} GB";

    private static string LimitOutput(string output)
    {
        const int maximumLength = 6000;
        return output.Length <= maximumLength
            ? output
            : output[..maximumLength] + Environment.NewLine + "[Output truncated by FixMate.]";
    }

    private sealed record DiagnosticCheckOutcome(
        DiagnosticStatus Status,
        string Summary,
        string TechnicalDetails,
        string RecommendedNextSteps,
        string PossibleCauses = "This check does not identify a cause.");

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetSystemExecutable(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    private static string FirstNonEmptyLine(params string[] values) => values
        .SelectMany(value => value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        .Select(line => line.Trim())
        .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "No command output was returned.";
}
