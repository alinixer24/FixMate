using System.Net;
using System.Net.NetworkInformation;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsNetworkDiagnosticsService : INetworkDiagnosticsService
{
    private const int PingTimeoutMilliseconds = 3000;
    private const int MaximumTraceHops = 20;

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<NetworkAdapterInfo>>(() =>
        {
            var adapters = new List<NetworkAdapterInfo>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var properties = adapter.GetIPProperties();
                    var dhcpEnabled = TryGetDhcpEnabled(properties);
                    var ipAddresses = properties.UnicastAddresses
                        .Select(address => address.Address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var gateways = properties.GatewayAddresses
                        .Select(gateway => gateway.Address.ToString())
                        .Where(address => !string.IsNullOrWhiteSpace(address) && address != "0.0.0.0" && address != "::")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var dnsServers = properties.DnsAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var isWifi = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    var wifiDetails = isWifi
                        ? "Wi-Fi adapter detected; SSID and signal details are not available through this provider."
                        : "Not a Wi-Fi adapter.";

                    adapters.Add(new NetworkAdapterInfo(
                        adapter.Name,
                        adapter.Description,
                        adapter.OperationalStatus.ToString(),
                        GetConnectionType(adapter.NetworkInterfaceType),
                        FormatMacAddress(adapter.GetPhysicalAddress().GetAddressBytes()),
                        adapter.Speed,
                        dhcpEnabled,
                        ipAddresses,
                        gateways,
                        dnsServers,
                        wifiDetails));
                }
                catch (NetworkInformationException)
                {
                    adapters.Add(new NetworkAdapterInfo(
                        adapter.Name,
                        adapter.Description,
                        "Details unavailable",
                        GetConnectionType(adapter.NetworkInterfaceType),
                        "Unavailable",
                        0,
                        null,
                        [],
                        [],
                        [],
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                            ? "Wi-Fi details could not be read."
                            : "Not a Wi-Fi adapter."));
                }
            }

            return adapters.OrderByDescending(adapter => adapter.Status == OperationalStatus.Up.ToString())
                .ThenBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public Task<NetworkDiagnosticResult> PingAsync(string host, CancellationToken cancellationToken = default)
    {
        ValidateTarget(host);
        return RunPingAsync(host, "Ping", cancellationToken);
    }

    public async Task<NetworkDiagnosticResult> TestConnectivityAsync(string host, CancellationToken cancellationToken = default)
    {
        ValidateTarget(host);
        var result = await RunPingAsync(host, "Connectivity test", cancellationToken);
        var summary = result.Succeeded
            ? $"The selected target {host} responded to ICMP. This does not verify every internet service."
            : $"The selected target {host} did not respond to ICMP. ICMP may be blocked even when internet access works.";
        return result with { Summary = summary };
    }

    public async Task<NetworkDiagnosticResult> TraceRouteAsync(string host, CancellationToken cancellationToken = default)
    {
        ValidateTarget(host);
        var targetAddress = await ResolveTargetAsync(host, cancellationToken);
        var details = new List<string>();
        using var ping = new Ping();
        for (var hop = 1; hop <= MaximumTraceHops; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new PingOptions(hop, true);
            try
            {
                var reply = await ping.SendPingAsync(targetAddress, TimeSpan.FromMilliseconds(PingTimeoutMilliseconds), new byte[32], options, cancellationToken);
                var address = reply.Address?.ToString() ?? "No reply";
                details.Add($"{hop,2}  {address}  {reply.RoundtripTime} ms  ({reply.Status})");
                if (reply.Status == IPStatus.Success)
                {
                    return CreateResult("Traceroute", host, true, $"Route reached {host} in {hop} hop(s).", details);
                }
            }
            catch (PingException exception)
            {
                details.Add($"{hop,2}  {exception.InnerException?.Message ?? exception.Message}");
            }
        }

        return CreateResult("Traceroute", host, false, $"The destination was not reached within {MaximumTraceHops} hops.", details);
    }

    public async Task<NetworkDiagnosticResult> LookupDnsAsync(string host, CancellationToken cancellationToken = default)
    {
        ValidateTarget(host);
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var details = addresses.Select(address => $"{address.AddressFamily}: {address}").ToArray();
        return CreateResult(
            "DNS lookup",
            host,
            details.Length > 0,
            details.Length > 0 ? $"Resolved {host} to {details.Length} address(es). The configured DNS resolver was contacted." : $"No addresses were returned for {host}.",
            details);
    }

    private static async Task<NetworkDiagnosticResult> RunPingAsync(string host, string tool, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var targetAddress = await ResolveTargetAsync(host, cancellationToken);
        var reply = await ping.SendPingAsync(targetAddress, TimeSpan.FromMilliseconds(PingTimeoutMilliseconds), new byte[32], new PingOptions(), cancellationToken);
        var address = reply.Address?.ToString() ?? "No address returned";
        var succeeded = reply.Status == IPStatus.Success;
        var details = new[] { $"Status: {reply.Status}", $"Address: {address}", $"Round-trip time: {reply.RoundtripTime} ms", $"Packet size: {reply.Buffer.Length} bytes" };
        return CreateResult(tool, host, succeeded, succeeded ? $"{host} responded in {reply.RoundtripTime} ms." : $"{host} did not respond: {reply.Status}.", details);
    }

    private static async Task<IPAddress> ResolveTargetAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"The target {host} did not resolve to an IP address.");
    }

    private static NetworkDiagnosticResult CreateResult(string tool, string host, bool succeeded, string summary, IReadOnlyList<string> details)
    {
        return new NetworkDiagnosticResult(tool, host, succeeded, summary, details, DateTimeOffset.Now);
    }

    private static void ValidateTarget(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (host.Length > 253 || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Enter a valid host name or IP address.", nameof(host));
        }
    }

    private static bool? TryGetDhcpEnabled(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.IsDhcpEnabled;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string GetConnectionType(NetworkInterfaceType type)
    {
        return type switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx => "Ethernet",
            NetworkInterfaceType.Loopback => "Loopback",
            NetworkInterfaceType.Tunnel => "Tunnel",
            _ => type.ToString()
        };
    }

    private static string FormatMacAddress(byte[] bytes)
    {
        return bytes.Length == 0 ? "Unavailable" : string.Join("-", bytes.Select(value => value.ToString("X2")));
    }
}
