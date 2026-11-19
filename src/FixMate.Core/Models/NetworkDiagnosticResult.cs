namespace FixMate.Core.Models;

public sealed record NetworkDiagnosticResult(
    string Tool,
    string Target,
    bool Succeeded,
    string Summary,
    IReadOnlyList<string> Details,
    DateTimeOffset CompletedAt);
