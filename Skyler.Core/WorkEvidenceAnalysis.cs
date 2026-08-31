namespace Skyler.Core;

public sealed class WorkEvidenceAnalysis
{
    // This is a stored-result compatibility version, not a calculation or
    // calendar-period counter. Increment it only when existing analyses can no
    // longer be read safely by the current application.
    public const int CurrentAnalysisVersion = 5;

    public Guid Id { get; set; }

    public Guid WorkEvidenceId { get; set; }

    public WorkEvidence? Evidence { get; set; }

    public string Analyzer { get; set; } = string.Empty;

    public bool UsedLocalModel { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? InferredRole { get; set; }

    public double RoleConfidence { get; set; }

    public string RoleRationale { get; set; } = string.Empty;

    public string? AutomationOpportunity { get; set; }

    public int? EstimatedTimeFreedMinutes { get; set; }

    public DateTimeOffset? AutomationApprovedAtUtc { get; set; }

    public int? ApprovedTimeFreedMinutes { get; set; }

    public int AnalysisVersion { get; set; } = CurrentAnalysisVersion;

    public DateTimeOffset AnalyzedAtUtc { get; set; }

    public string? Warning { get; set; }

    public ICollection<DimensionAssessment> Dimensions { get; set; } = [];

    public int TimeFreedMinutes =>
        AutomationApprovedAtUtc.HasValue
            ? Math.Max(0, ApprovedTimeFreedMinutes ?? 0)
            : 0;
}
