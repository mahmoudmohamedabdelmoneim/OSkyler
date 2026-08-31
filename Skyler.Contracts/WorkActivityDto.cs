namespace Skyler.Contracts;

public sealed record WorkActivityDto(
    Guid Id,
    string Description,
    string Classification,
    string HumanOpportunity,
    int BaselineMinutes,
    int ActualMinutes,
    DateTimeOffset? AutomationApprovedAtUtc,
    int TimeFreedMinutes,
    DateTimeOffset OccurredAtUtc);
