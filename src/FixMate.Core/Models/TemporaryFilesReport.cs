namespace FixMate.Core.Models;

public sealed record TemporaryFilesReport(
    int CandidateCount,
    long CandidateBytes,
    int SkippedCount,
    int DeletedCount = 0,
    long DeletedBytes = 0);
