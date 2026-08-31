namespace Skyler.Core;

public interface IWorkEvidenceAnalyzer
{
    Task<WorkEvidenceAnalysis> AnalyzeAsync(
        WorkEvidence evidence,
        CancellationToken cancellationToken = default);
}
