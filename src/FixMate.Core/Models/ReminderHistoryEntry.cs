namespace FixMate.Core.Models;

public sealed record ReminderHistoryEntry(
    Guid ReminderId,
    string ReminderName,
    DateTimeOffset CompletedAt,
    DateTimeOffset? NextDueAt);
