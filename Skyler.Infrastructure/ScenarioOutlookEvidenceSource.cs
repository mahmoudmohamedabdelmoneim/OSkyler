using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class ScenarioOutlookEvidenceSource : IWorkEvidenceSource
{
    public Task<IReadOnlyList<WorkEvidence>> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<WorkEvidence> evidence =
        [
            new WorkEvidence
            {
                Id = Guid.Parse("410e813d-846a-4b28-890e-1f872589ef59"),
                ExternalId = "scenario-outlook-email-weekly-status",
                EmployeeId = "demo.employee@skyler.local",
                Source = EvidenceSource.Outlook,
                Kind = EvidenceKind.Email,
                Subject = "Weekly status, resolved reporting issue, and next actions",
                Content = """
                    Preparing the weekly status report requires collecting recurring updates and drafting the same sections.
                    The preparation usually takes 45 minutes before the final review and send.
                    I resolved the reporting issue, offered to help the operations team, and documented action items.
                    The short-term priority is validating the next report; the long-term goal is reusable self-service reporting.
                    """,
                Participants = "Demo employee; Operations team",
                OccurredAtUtc = now.AddHours(-2),
                BaselineMinutes = 45,
                IsSynthetic = true
            },
            new WorkEvidence
            {
                Id = Guid.Parse("cad3ddc6-8f2f-45bf-af9a-d4c704f33dfa"),
                ExternalId = "scenario-outlook-meeting-mentorship",
                EmployeeId = "demo.employee@skyler.local",
                Source = EvidenceSource.Outlook,
                Kind = EvidenceKind.CalendarMeeting,
                Subject = "Mentoring check-in: incident response skills",
                Content = """
                    Mentoring session focused on listening to the mentee's recent challenges, reviewing root-cause analysis,
                    and coaching a repeatable incident-response approach. Agreed on a skill goal and a follow-up practice task.
                    """,
                Participants = "Demo employee; Demo mentee",
                OccurredAtUtc = now.AddDays(-1),
                DurationMinutes = 60,
                IsMentorship = true,
                IsSynthetic = true
            }
        ];

        return Task.FromResult(evidence);
    }
}
