using System.Globalization;
using System.Management;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsDriverInformationProvider : IDriverInformationProvider
{
    public Task<IReadOnlyList<InstalledDriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<InstalledDriverInfo>>(() =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, Manufacturer, DriverProviderName, DriverVersion, DriverDate, DeviceClass, InfName, IsSigned FROM Win32_PnPSignedDriver");
            using var results = searcher.Get();
            var drivers = new List<InstalledDriverInfo>();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    drivers.Add(new InstalledDriverInfo
                    {
                        DeviceName = Value(item, "DeviceName"),
                        Manufacturer = Value(item, "Manufacturer"),
                        DriverProvider = Value(item, "DriverProviderName"),
                        DriverVersion = Value(item, "DriverVersion"),
                        DriverDate = ParseDriverDate(Value(item, "DriverDate")),
                        DeviceClass = Value(item, "DeviceClass"),
                        InfName = Value(item, "InfName"),
                        IsSigned = bool.TryParse(Value(item, "IsSigned"), out var isSigned) && isSigned
                    });
                }
            }

            return drivers
                .OrderBy(driver => driver.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    private static string Value(ManagementBaseObject item, string propertyName)
    {
        return Convert.ToString(item[propertyName], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static DateTimeOffset? ParseDriverDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
