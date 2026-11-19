namespace FixMate.Core.Models;

public sealed record InstalledApplication(
    string Name,
    string Version,
    string Publisher,
    DateTime? InstallDate,
    string RegistryView,
    string InstallLocation = "",
    string DisplayIcon = "",
    string PublisherWebsite = "")
{
    public bool HasVersionInformation => !string.IsNullOrWhiteSpace(Version);
    public string VersionInformationStatus => HasVersionInformation ? "Available" : "Not provided";
}
