namespace Skyler.Contracts;

public sealed record DashboardSummaryDto(
    DateTimeOffset GeneratedAtUtc,
    string DataMode,
    string Mailbox,
    string Period,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    int EvidenceCount,
    int DecidedObservationCount,
    int TimeFreedMinutes,
    int WorkdayBaselineMinutes,
    int PeriodBaselineMinutes,
    double TimeFreedPercentage,
    int AutomationOpportunityCount,
    int ApprovedAutomationCount,
    int PendingAutomationApprovalCount,
    int AbsenceObservationCount,
    int MentorshipMinutes,
    IReadOnlyList<AiWorkItemDto> AiWorkItems,
    MailboxRoleSummaryDto Role,
    IReadOnlyList<DimensionSummaryDto> DimensionScores,
    IReadOnlyList<EvidenceAnalysisDto> RecentAnalyses);

public sealed record AiWorkItemDto(
    Guid EvidenceId,
    string Subject,
    string WorkDescription,
    int Minutes,
    DateTimeOffset ApprovedAtUtc);

public sealed record MailboxRoleSummaryDto(
    string Decision,
    string? Title,
    double Confidence,
    int EvidenceCount,
    string Rationale);

public sealed record DimensionSummaryDto(
    string Dimension,
    string DisplayName,
    double? Percentage,
    double? Confidence,
    int EvidenceCount);

public sealed record EvidenceAnalysisDto(
    Guid EvidenceId,
    string Source,
    string Kind,
    string Subject,
    DateTimeOffset OccurredAtUtc,
    int? DurationMinutes,
    bool IsMentorship,
    bool IsSynthetic,
    bool IsAbsent,
    string Summary,
    string? AutomationOpportunity,
    string Analyzer,
    bool UsedLocalModel,
    int? EstimatedTimeFreedMinutes,
    bool AutomationApproved,
    DateTimeOffset? AutomationApprovedAtUtc,
    int TimeFreedMinutes,
    string? Warning,
    IReadOnlyList<DimensionScoreDto> Dimensions);

public sealed record DimensionScoreDto(
    string Dimension,
    string DisplayName,
    int? Percentage,
    double Confidence,
    string Rationale);

public sealed record AutomationApprovalRequestDto(bool Approved);
