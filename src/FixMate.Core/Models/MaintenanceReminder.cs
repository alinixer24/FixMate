namespace FixMate.Core.Models;

public sealed record MaintenanceReminder(
    Guid Id,
    string Name,
    DateTimeOffset DueAt,
    string RepeatInterval,
    bool IsEnabled,
    bool Notify);
