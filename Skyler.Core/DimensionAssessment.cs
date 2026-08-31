namespace Skyler.Core;

public sealed class DimensionAssessment
{
    public Guid Id { get; set; }

    public Guid WorkEvidenceAnalysisId { get; set; }

    public WorkEvidenceAnalysis? Analysis { get; set; }

    public HumanWorkDimension Dimension { get; set; }

    public int? Score { get; set; }

    public double Confidence { get; set; }

    public string Rationale { get; set; } = string.Empty;
}
