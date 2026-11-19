namespace FixMate.Core.Models;

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    string Status,
    string ConnectionType,
    string MacAddress,
    long LinkSpeedBitsPerSecond,
    bool? DhcpEnabled,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    string WifiDetails);
