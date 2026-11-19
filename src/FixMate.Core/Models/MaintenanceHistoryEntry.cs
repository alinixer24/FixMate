namespace FixMate.Core.Models;

public sealed record MaintenanceHistoryEntry(
    Guid Id,
    string Operation,
    string Outcome,
    string Details,
    DateTimeOffset CompletedAt);
