namespace FixMate.Core.Models;

public sealed class DiagnosticResult
{
    public DiagnosticArea Area { get; set; }
    public string TestName { get; set; } = string.Empty;
    public DiagnosticStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; set; }
}
