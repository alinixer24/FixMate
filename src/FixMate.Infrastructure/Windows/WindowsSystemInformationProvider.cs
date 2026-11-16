using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using FixMate.Core.Results;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsSystemInformationProvider : ISystemInformationProvider
{
    public Task<SystemInformationSnapshot> GetSystemInformationAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Collect(cancellationToken), cancellationToken);
    }

    private static SystemInformationSnapshot Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operatingSystem = ReadOperatingSystem();

        cancellationToken.ThrowIfCancellationRequested();
        var processor = ReadSingleComponent(
            "CPU",
            "SELECT Name, Manufacturer, Architecture, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor",
            item => Detected(
                "CPU",
                "Processor",
                Value(item, "Manufacturer"),
                Value(item, "Name"),
                WithOptionalDetail(
                    "Architecture", FormatProcessorArchitecture(Value(item, "Architecture")),
                    "Cores", Value(item, "NumberOfCores"),
                    "Logical processors", Value(item, "NumberOfLogicalProcessors"),
                    "Maximum clock", FormatClockSpeed(Value(item, "MaxClockSpeed")),
                    "L2 cache", FormatCacheSize(Value(item, "L2CacheSize")),
                    "L3 cache", FormatCacheSize(Value(item, "L3CacheSize")))));

        cancellationToken.ThrowIfCancellationRequested();
        var memory = ReadMemory();

        cancellationToken.ThrowIfCancellationRequested();
        var graphics = ReadMultipleComponents(
            "Graphics",
            "SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController",
            item => Detected(
                "GPU",
                "Graphics adapter",
                Value(item, "AdapterCompatibility"),
                Value(item, "Name"),
                WithOptionalDetail("Driver version", Value(item, "DriverVersion"))));

        cancellationToken.ThrowIfCancellationRequested();
        var storage = ReadMultipleComponents(
            "Storage",
            "SELECT Model, Manufacturer, Size, MediaType FROM Win32_DiskDrive",
            item => Detected(
                "Storage",
                "Disk drive",
                Value(item, "Manufacturer"),
                Value(item, "Model"),
                WithOptionalDetail("Capacity", FormatBytes(ParseLong(Value(item, "Size"))), "Media", Value(item, "MediaType"))));

        cancellationToken.ThrowIfCancellationRequested();
        var motherboard = ReadSingleComponent(
            "Motherboard",
            "SELECT Manufacturer, Product FROM Win32_BaseBoard",
            item => Detected("Motherboard", "Baseboard", Value(item, "Manufacturer"), Value(item, "Product")));

        cancellationToken.ThrowIfCancellationRequested();
        var bios = ReadSingleComponent(
            "BIOS",
            "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
            item => Detected(
                "BIOS",
                "BIOS",
                Value(item, "Manufacturer"),
                Value(item, "SMBIOSBIOSVersion"),
                WithOptionalDetail("Release date", FormatWmiDate(Value(item, "ReleaseDate")))));

        var components = new List<HardwareComponent> { processor, bios };
        components.AddRange(memory);
        components.AddRange(graphics);
        components.AddRange(storage);
        components.Add(motherboard);

        return new SystemInformationSnapshot
        {
            OperatingSystem = operatingSystem,
            Components = components,
            CollectedAt = DateTimeOffset.Now
        };
    }

    private static OperatingSystemInfo ReadOperatingSystem()
    {
        try
        {
            var item = ReadQuery(
                "SELECT Caption, Version, OSArchitecture FROM Win32_OperatingSystem",
                result => new OperatingSystemInfo
                {
                    Name = Value(result, "Caption"),
                    Version = Value(result, "Version"),
                    Architecture = Value(result, "OSArchitecture"),
                    MachineName = Environment.MachineName,
                    Status = DetectionStatus.Detected
                }).FirstOrDefault();

            return item ?? new OperatingSystemInfo
            {
                MachineName = Environment.MachineName,
                Status = DetectionStatus.NotDetected,
                Details = "Windows did not return operating system information."
            };
        }
        catch (Exception exception) when (IsWmiAccessException(exception))
        {
            return new OperatingSystemInfo
            {
                MachineName = Environment.MachineName,
                Status = DetectionStatus.Error,
                Details = $"Could not read Windows details: {exception.Message}"
            };
        }
    }

    private static IReadOnlyList<HardwareComponent> ReadMemory()
    {
        return ReadComponentGroup("RAM", () =>
        {
            var modules = ReadQuery(
                "SELECT Capacity, Manufacturer, PartNumber, DeviceLocator, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory",
                item => new MemoryModule(
                    ParseLong(Value(item, "Capacity")),
                    Value(item, "Manufacturer"),
                    Value(item, "PartNumber"),
                    Value(item, "DeviceLocator"),
                    Value(item, "Speed"),
                    Value(item, "ConfiguredClockSpeed"),
                    Value(item, "SMBIOSMemoryType")));
            if (modules.Count == 0)
            {
                return [Missing("RAM")];
            }

            var totalBytes = modules.Sum(module => module.Capacity);
            var manufacturers = string.Join(", ", modules
                .Select(module => module.Manufacturer)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var components = new List<HardwareComponent>
            {
                Detected(
                    "RAM",
                    "Installed memory",
                    manufacturers,
                    $"{FormatBytes(totalBytes)} across {modules.Count} module(s)")
            };

            for (var index = 0; index < modules.Count; index++)
            {
                var module = modules[index];
                var location = string.IsNullOrWhiteSpace(module.DeviceLocator)
                    ? $"Memory module {index + 1}"
                    : module.DeviceLocator;
                var model = string.IsNullOrWhiteSpace(module.PartNumber)
                    ? "Memory module"
                    : module.PartNumber;
                components.Add(Detected(
                    "RAM module",
                    location,
                    module.Manufacturer,
                    model,
                    WithOptionalDetail(
                        "Capacity", FormatBytes(module.Capacity),
                        "Configured speed", FormatClockSpeed(module.ConfiguredClockSpeed),
                        "Reported speed", FormatClockSpeed(module.Speed),
                        "Memory type", FormatMemoryType(module.MemoryType))));
            }

            return components;
        });
    }

    private static HardwareComponent ReadSingleComponent(
        string category,
        string query,
        Func<ManagementBaseObject, HardwareComponent> map)
    {
        return ReadComponentGroup(category, () =>
        {
            var component = ReadQuery(query, map).FirstOrDefault();
            return component is null ? [Missing(category)] : [component];
        }).Single();
    }

    private static IReadOnlyList<HardwareComponent> ReadMultipleComponents(
        string category,
        string query,
        Func<ManagementBaseObject, HardwareComponent> map)
    {
        return ReadComponentGroup(category, () =>
        {
            var components = ReadQuery(query, map);
            return components.Count == 0 ? [Missing(category)] : components;
        });
    }

    private static IReadOnlyList<HardwareComponent> ReadComponentGroup(
        string category,
        Func<IReadOnlyList<HardwareComponent>> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsWmiAccessException(exception))
        {
            return [new HardwareComponent
            {
                Category = category,
                Name = category,
                Status = DetectionStatus.Error,
                Details = $"Could not detect {category.ToLowerInvariant()}: {exception.Message}"
            }];
        }
    }

    private static List<T> ReadQuery<T>(string query, Func<ManagementBaseObject, T> map)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        var items = new List<T>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                items.Add(map(item));
            }
        }

        return items;
    }

    private static HardwareComponent Detected(
        string category,
        string name,
        string manufacturer,
        string model,
        string details = "")
    {
        return new HardwareComponent
        {
            Category = category,
            Name = name,
            Manufacturer = manufacturer,
            Model = model,
            Status = DetectionStatus.Detected,
            Details = details
        };
    }

    private static HardwareComponent Missing(string category)
    {
        return new HardwareComponent
        {
            Category = category,
            Name = category,
            Status = DetectionStatus.NotDetected,
            Details = $"No {category.ToLowerInvariant()} information was returned by Windows."
        };
    }

    private static string Value(ManagementBaseObject item, string propertyName)
    {
        return Convert.ToString(item[propertyName], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "Capacity unavailable";
        }

        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return $"{gibibytes:N1} GB";
    }

    private static string FormatClockSpeed(string value)
    {
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var megahertz) && megahertz > 0
            ? $"{megahertz:N0} MHz"
            : string.Empty;
    }

    private static string FormatCacheSize(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kilobytes) && kilobytes > 0
            ? $"{kilobytes:N0} KB"
            : string.Empty;
    }

    private static string FormatProcessorArchitecture(string value)
    {
        return value switch
        {
            "0" => "x86",
            "5" => "ARM",
            "9" => "x64",
            "12" => "ARM64",
            _ => string.Empty
        };
    }

    private static string FormatMemoryType(string value)
    {
        return value switch
        {
            "20" => "DDR",
            "21" => "DDR2",
            "24" => "DDR3",
            "26" => "DDR4",
            "27" => "LPDDR",
            "28" => "LPDDR2",
            "29" => "LPDDR3",
            "30" => "LPDDR4",
            "34" => "DDR5",
            "35" => "LPDDR5",
            _ => string.Empty
        };
    }

    private static string FormatWmiDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value).ToString("d", CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string WithOptionalDetail(params string[] values)
    {
        var details = new List<string>();
        for (var index = 0; index + 1 < values.Length; index += 2)
        {
            if (!string.IsNullOrWhiteSpace(values[index + 1]))
            {
                details.Add($"{values[index]}: {values[index + 1]}");
            }
        }

        return string.Join(" · ", details);
    }

    private static string WithOptionalDetail(string label, string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";
    }

    private static bool IsWmiAccessException(Exception exception)
    {
        return exception is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException;
    }

    private sealed record MemoryModule(
        long Capacity,
        string Manufacturer,
        string PartNumber,
        string DeviceLocator,
        string Speed,
        string ConfiguredClockSpeed,
        string MemoryType);
}
