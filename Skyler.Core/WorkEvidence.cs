namespace Skyler.Core;

public sealed class WorkEvidence
{
    public Guid Id { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string EmployeeId { get; set; } = string.Empty;

    public EvidenceSource Source { get; set; } = EvidenceSource.Outlook;

    public EvidenceKind Kind { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Participants { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public int? DurationMinutes { get; set; }

    public int? BaselineMinutes { get; set; }

    public int? ActualMinutes { get; set; }

    public bool IsMentorship { get; set; }

    public bool IsSynthetic { get; set; }

    public bool IsAbsence { get; set; }

    public WorkEvidenceAnalysis? Analysis { get; set; }
}
