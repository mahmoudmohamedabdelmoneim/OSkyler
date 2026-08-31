namespace Skyler.Infrastructure;

public sealed record OutlookOptions(
    string Mode,
    string ClientId,
    string Mailbox,
    string Authority,
    int SyncDays,
    int MaxItems,
    IReadOnlyList<string> MentorshipIndicators,
    IReadOnlyList<string> MentorshipMeetingLinkIndicators);
