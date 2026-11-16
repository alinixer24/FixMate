namespace FixMate.Core.Models;

public sealed record MetricReading(double? Value, string Details)
{
    public bool IsAvailable => Value.HasValue;
}
