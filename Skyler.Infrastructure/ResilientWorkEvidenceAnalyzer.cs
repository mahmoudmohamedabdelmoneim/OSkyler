using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class ResilientWorkEvidenceAnalyzer(
    OllamaWorkEvidenceAnalyzer localModelAnalyzer,
    ScenarioEvidenceAnalyzer scenarioAnalyzer) : IWorkEvidenceAnalyzer
{
    public Task<WorkEvidenceAnalysis> AnalyzeAsync(
        WorkEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        if (evidence.IsAbsence || evidence.IsSynthetic)
        {
            return scenarioAnalyzer.AnalyzeAsync(evidence, cancellationToken);
        }

        return localModelAnalyzer.AnalyzeAsync(evidence, cancellationToken);
    }
}
