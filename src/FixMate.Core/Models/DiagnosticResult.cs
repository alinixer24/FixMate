namespace FixMate.Core.Models;

public sealed class DiagnosticResult
{
    public DiagnosticArea Area { get; set; }
    public string TestName { get; set; } = string.Empty;
    public DiagnosticStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string PossibleCauses { get; set; } = "This check does not identify a cause.";
    public string TechnicalDetails { get; set; } = string.Empty;
    public string RecommendedNextSteps { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool CanRunAction => Area is DiagnosticArea.Network or DiagnosticArea.Storage or DiagnosticArea.WindowsIntegrity;
}
