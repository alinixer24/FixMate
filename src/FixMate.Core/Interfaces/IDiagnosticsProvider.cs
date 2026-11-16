using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IDiagnosticsProvider
{
    Task<IReadOnlyList<DiagnosticResult>> RunDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<string> RunRepairAsync(DiagnosticArea area, CancellationToken cancellationToken = default);
}
