namespace Skyler.Core;

public sealed class WorkActivity
{
    public Guid Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;

    public string HumanOpportunity { get; set; } = string.Empty;

    public int BaselineMinutes { get; set; }

    public int ActualMinutes { get; set; }

    public DateTimeOffset? AutomationApprovedAtUtc { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public int TimeFreedMinutes => AutomationApprovedAtUtc.HasValue
        ? Math.Max(0, BaselineMinutes - ActualMinutes)
        : 0;
}
