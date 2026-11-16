namespace FixMate.Core.Models;

public sealed record WindowsEventEntry(
    string LogName,
    long? RecordId,
    int EventId,
    string Level,
    string Provider,
    DateTimeOffset? TimeCreated,
    string MachineName,
    string Message)
{
    public string TechnicalDetails =>
        $"Log: {LogName}{Environment.NewLine}" +
        $"Record ID: {RecordId?.ToString() ?? "Unavailable"}{Environment.NewLine}" +
        $"Event ID: {EventId}{Environment.NewLine}" +
        $"Level: {Level}{Environment.NewLine}" +
        $"Provider: {Provider}{Environment.NewLine}" +
        $"Computer: {MachineName}";
}
