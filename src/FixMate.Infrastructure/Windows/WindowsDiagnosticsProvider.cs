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

    public Task<IReadOnlyList<DiagnosticResult>> RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DiagnosticResult>>(async () =>
        [
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
        Func<CancellationToken, Task<(DiagnosticStatus Status, string Summary)>> check,
        CancellationToken cancellationToken)
    {
        try
        {
            var (status, summary) = await check(cancellationToken);
            return Result(area, status, summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result(area, DiagnosticStatus.Inconclusive, $"The check could not complete: {exception.Message}");
        }
    }

    private static async Task<(DiagnosticStatus Status, string Summary)> CheckNetworkAsync(CancellationToken cancellationToken)
    {
        var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();
        if (activeAdapters.Length == 0)
        {
            return (DiagnosticStatus.Failed, "No active network adapter was found.");
        }

        var hasGateway = activeAdapters.Any(adapter => adapter.GetIPProperties().GatewayAddresses
            .Any(gateway => !IPAddress.IsLoopback(gateway.Address) && !gateway.Address.Equals(IPAddress.Any)));
        if (!hasGateway)
        {
            return (DiagnosticStatus.Warning, $"Found {activeAdapters.Length} active adapter(s), but none has a default gateway.");
        }

        var addresses = await Dns.GetHostAddressesAsync("www.microsoft.com", cancellationToken);
        return addresses.Length > 0
            ? (DiagnosticStatus.Passed, $"DNS resolution succeeded through {activeAdapters.Length} active adapter(s).")
            : (DiagnosticStatus.Warning, "The network is active, but DNS returned no addresses.");
    }

    private static Task<(DiagnosticStatus Status, string Summary)> CheckStorageAsync(CancellationToken cancellationToken)
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
            return Task.FromResult((DiagnosticStatus.Warning, "Windows reported no ready fixed drives."));
        }

        var lowSpace = fixedDrives
            .Where(drive => drive.TotalSize > 0 && drive.AvailableFreeSpace / (double)drive.TotalSize < 0.1)
            .Select(drive => $"{drive.Name} ({drive.AvailableFreeSpace / (1024d * 1024 * 1024):N1} GB free)")
            .ToArray();
        var diskStatuses = ReadDiskStatuses();
        var reportedProblem = diskStatuses.Any(status =>
            !string.IsNullOrWhiteSpace(status) &&
            !status.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("Unknown", StringComparison.OrdinalIgnoreCase));

        var summary = $"Checked {fixedDrives.Length} fixed drive(s) and Windows disk status.";
        if (lowSpace.Length > 0)
        {
            summary += $" Low free space: {string.Join(", ", lowSpace)}.";
        }

        if (reportedProblem)
        {
            summary += $" Windows reported disk status: {string.Join(", ", diskStatuses.Where(status => !string.IsNullOrWhiteSpace(status)).Distinct())}.";
        }

        return Task.FromResult((
            lowSpace.Length > 0 || reportedProblem ? DiagnosticStatus.Warning : DiagnosticStatus.Passed,
            summary));
    }

    private static Task<(DiagnosticStatus Status, string Summary)> CheckWindowsIntegrityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAdministrator())
        {
            return Task.FromResult((
                DiagnosticStatus.Inconclusive,
                "Run FixMate as administrator to check the Windows component store and protected system files."));
        }

        return CheckWindowsIntegrityAsAdministratorAsync(cancellationToken);
    }

    private static async Task<(DiagnosticStatus Status, string Summary)> CheckWindowsIntegrityAsAdministratorAsync(
        CancellationToken cancellationToken)
    {
        var dism = await RunCapturedCommandAsync(
            GetSystemExecutable("dism.exe"), "/Online /Cleanup-Image /CheckHealth", cancellationToken);
        var sfc = await RunCapturedCommandAsync(
            GetSystemExecutable("sfc.exe"), "/verifyonly", cancellationToken);

        if (dism.ExitCode != 0 || sfc.ExitCode != 0)
        {
            var output = FirstNonEmptyLine(dism.Output, sfc.Output);
            return (DiagnosticStatus.Warning,
                $"DISM exited with code {dism.ExitCode}; SFC exited with code {sfc.ExitCode}. {output}");
        }

        return (DiagnosticStatus.Passed,
            "DISM and System File Checker completed their verification checks without reporting an error exit code.");
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

    private static DiagnosticResult Result(DiagnosticArea area, DiagnosticStatus status, string summary)
    {
        return new DiagnosticResult
        {
            Area = area,
            TestName = GetTestName(area),
            Status = status,
            Summary = summary,
            CompletedAt = DateTimeOffset.Now
        };
    }

    private static string GetTestName(DiagnosticArea area) => area switch
    {
        DiagnosticArea.Network => "Network connectivity",
        DiagnosticArea.Storage => "Storage health",
        DiagnosticArea.WindowsIntegrity => "Windows integrity",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown diagnostic area.")
    };

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
