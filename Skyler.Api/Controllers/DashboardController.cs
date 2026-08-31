using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skyler.Contracts;
using Skyler.Core;
using Skyler.Infrastructure;

namespace Skyler.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(
    SkylerDbContext database,
    IConfiguration configuration,
    OutlookAnalysisWorker outlookAnalysisWorker,
    OutlookTokenProvider outlookTokenProvider) : ControllerBase
{
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        if (outlookTokenProvider.AuthorizationRequired)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Outlook authorization is required. The cached dashboard remains available while an administrator completes sign-in.");
        }

        await outlookAnalysisWorker.SynchronizeNowAsync(cancellationToken);
        outlookAnalysisWorker.QueueAnalysis();
        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> Get(
        [FromQuery] string period = "week",
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedPeriod = NormalizePeriod(period);
        var periodStart = GetPeriodStart(normalizedPeriod, now);
        var authorizedMailbox = configuration["Dashboard:Mailbox"]
            ?? throw new InvalidOperationException(
                "Configuration value 'Dashboard:Mailbox' was not found.");

        var allEvidence = await database.WorkEvidence
            .AsNoTracking()
            .Include(evidence => evidence.Analysis)
                .ThenInclude(analysis => analysis!.Dimensions)
            .Where(evidence => evidence.EmployeeId == authorizedMailbox)
            .ToListAsync(cancellationToken);

        var evidenceInPeriod = allEvidence
            .Where(evidence =>
                !evidence.IsSynthetic &&
                evidence.OccurredAtUtc >= periodStart &&
                evidence.OccurredAtUtc <= now &&
                HasCurrentAnalysis(evidence))
            .OrderByDescending(evidence => evidence.OccurredAtUtc)
            .ToList();
        var workEvidenceInPeriod = evidenceInPeriod
            .Where(evidence => !evidence.IsAbsence)
            .ToList();
        var analyses = workEvidenceInPeriod
            .Select(evidence => evidence.Analysis!)
            .ToList();
        var roleAnalyses = allEvidence
            .Where(evidence =>
                !evidence.IsSynthetic &&
                !evidence.IsAbsence &&
                HasCurrentAnalysis(evidence))
            .Select(evidence => evidence.Analysis!)
            .ToList();
        var approvedAutomationsInPeriod = allEvidence
            .Where(evidence =>
                !evidence.IsSynthetic &&
                !evidence.IsAbsence &&
                evidence.Analysis?.AnalysisVersion >= WorkEvidenceAnalysis.CurrentAnalysisVersion &&
                evidence.Analysis.AutomationApprovedAtUtc >= periodStart &&
                evidence.Analysis.AutomationApprovedAtUtc <= now)
            .ToList();
        var automationOpportunities = analyses
            .Where(analysis =>
                !string.IsNullOrWhiteSpace(analysis.AutomationOpportunity) &&
                analysis.EstimatedTimeFreedMinutes is > 0)
            .ToList();
        const int workdayBaselineMinutes = 8 * 60;
        var periodBaselineMinutes = CountWorkdays(periodStart, now) * workdayBaselineMinutes;
        var totalTimeFreed = approvedAutomationsInPeriod.Sum(evidence => evidence.Analysis!.TimeFreedMinutes);
        var aiWorkItems = approvedAutomationsInPeriod
            .OrderByDescending(evidence => evidence.Analysis!.AutomationApprovedAtUtc)
            .Select(evidence => new AiWorkItemDto(
                evidence.Id,
                evidence.Subject,
                evidence.Analysis!.AutomationOpportunity!,
                evidence.Analysis.TimeFreedMinutes,
                evidence.Analysis.AutomationApprovedAtUtc!.Value))
            .ToList();

        var dimensionSummaries = Enum.GetValues<HumanWorkDimension>()
            .Select(dimension => CreateDimensionSummary(dimension, analyses))
            .ToList();
        var roleSummary = CreateRoleSummary(roleAnalyses);
        var recentAnalyses = evidenceInPeriod
            .Select(CreateEvidenceAnalysis)
            .ToList();

        return Ok(new DashboardSummaryDto(
            now,
            "Live Outlook",
            authorizedMailbox,
            normalizedPeriod,
            periodStart,
            now,
            evidenceInPeriod.Count,
            analyses.Count(HasDecision),
            totalTimeFreed,
            workdayBaselineMinutes,
            periodBaselineMinutes,
            periodBaselineMinutes > 0
                ? Math.Round(totalTimeFreed * 100d / periodBaselineMinutes, 1)
                : 0,
            automationOpportunities.Count,
            approvedAutomationsInPeriod.Count,
            automationOpportunities.Count(analysis => !analysis.AutomationApprovedAtUtc.HasValue),
            evidenceInPeriod.Count(evidence => evidence.IsAbsence),
            evidenceInPeriod
                .Where(evidence => evidence.IsMentorship)
                .Sum(evidence => evidence.DurationMinutes ?? 0),
            aiWorkItems,
            roleSummary,
            dimensionSummaries,
            recentAnalyses));
    }

    private static bool HasCurrentAnalysis(WorkEvidence evidence) =>
        evidence.Analysis is
        {
            AnalysisVersion: >= WorkEvidenceAnalysis.CurrentAnalysisVersion
        } &&
        (evidence.IsSynthetic || evidence.IsAbsence || evidence.Analysis.UsedLocalModel);

    private static bool HasDecision(WorkEvidenceAnalysis analysis) =>
        analysis.Dimensions.Count == Enum.GetValues<HumanWorkDimension>().Length &&
        analysis.Dimensions.All(assessment => assessment.Score.HasValue);

    private static int CountWorkdays(DateTimeOffset start, DateTimeOffset end)
    {
        var count = 0;
        for (var date = start.UtcDateTime.Date; date <= end.UtcDateTime.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }

    private static string NormalizePeriod(string period) =>
        period.ToLowerInvariant() switch
        {
            "day" => "day",
            "month" => "month",
            _ => "week"
        };

    private static DateTimeOffset GetPeriodStart(string period, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        return period switch
        {
            "day" => today,
            "month" => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero),
            _ => today.AddDays(-6)
        };
    }

    private static DimensionSummaryDto CreateDimensionSummary(
        HumanWorkDimension dimension,
        IReadOnlyCollection<WorkEvidenceAnalysis> analyses)
    {
        var scoredAssessments = analyses
            .SelectMany(analysis => analysis.Dimensions)
            .Where(assessment => assessment.Dimension == dimension && assessment.Score.HasValue)
            .ToList();

        return new DimensionSummaryDto(
            dimension.ToString(),
            GetDisplayName(dimension),
            scoredAssessments.Count == 0
                ? null
                : Math.Round(scoredAssessments.Average(assessment => assessment.Score!.Value), 1),
            scoredAssessments.Count == 0
                ? null
                : Math.Round(scoredAssessments.Average(assessment => assessment.Confidence), 2),
            scoredAssessments.Count);
    }

    private static MailboxRoleSummaryDto CreateRoleSummary(
        IReadOnlyCollection<WorkEvidenceAnalysis> analyses)
    {
        var decidedRoles = analyses
            .Where(analysis =>
                !string.IsNullOrWhiteSpace(analysis.InferredRole) &&
                analysis.RoleConfidence > 0)
            .ToList();

        if (decidedRoles.Count == 0)
        {
            return new MailboxRoleSummaryDto(
                "undecided",
                null,
                0,
                0,
                "The available Outlook observations do not yet show enough role-distinguishing responsibilities to decide.");
        }

        var leadingRole = decidedRoles
            .GroupBy(
                analysis => analysis.InferredRole!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(analysis => analysis.RoleConfidence))
            .ThenByDescending(group => group.Count())
            .First();
        var representative = leadingRole
            .OrderByDescending(analysis => analysis.RoleConfidence)
            .First();

        return new MailboxRoleSummaryDto(
            "decided",
            leadingRole.Key,
            Math.Round(leadingRole.Average(analysis => analysis.RoleConfidence), 2),
            leadingRole.Count(),
            representative.RoleRationale);
    }

    private static EvidenceAnalysisDto CreateEvidenceAnalysis(WorkEvidence evidence)
    {
        var analysis = evidence.Analysis!;

        return new EvidenceAnalysisDto(
            evidence.Id,
            evidence.Source.ToString(),
            evidence.Kind.ToString(),
            evidence.Subject,
            evidence.OccurredAtUtc,
            evidence.DurationMinutes,
            evidence.IsMentorship,
            evidence.IsSynthetic,
            evidence.IsAbsence,
            analysis.Summary,
            analysis.AutomationOpportunity,
            analysis.Analyzer,
            analysis.UsedLocalModel,
            analysis.EstimatedTimeFreedMinutes,
            analysis.AutomationApprovedAtUtc.HasValue,
            analysis.AutomationApprovedAtUtc,
            analysis.TimeFreedMinutes,
            analysis.Warning,
            analysis.Dimensions
                .OrderBy(assessment => assessment.Dimension)
                .Select(assessment => new DimensionScoreDto(
                    assessment.Dimension.ToString(),
                    GetDisplayName(assessment.Dimension),
                    assessment.Score,
                    assessment.Confidence,
                    assessment.Score.HasValue
                        ? ContextualizeRationale(evidence, assessment.Rationale)
                        : evidence.IsAbsence
                            ? $"Not calculated because this {GetEvidenceLabel(evidence.Kind)} marks the employee absent."
                            : $"Not enough observable detail in this {GetEvidenceLabel(evidence.Kind)} to decide."))
                .ToList());
    }

    private static string ContextualizeRationale(WorkEvidence evidence, string rationale)
    {
        var contextualSubject = $"This {GetEvidenceLabel(evidence.Kind)}";

        return rationale
            .Replace("The Outlook item", contextualSubject, StringComparison.OrdinalIgnoreCase)
            .Replace("This Outlook item", contextualSubject, StringComparison.OrdinalIgnoreCase)
            .Replace("The item", contextualSubject, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetEvidenceLabel(EvidenceKind kind) => kind switch
    {
        EvidenceKind.Email => "Email",
        EvidenceKind.CalendarMeeting => "Teams meeting",
        _ => HumanizeEvidenceKind(kind.ToString())
    };

    private static string HumanizeEvidenceKind(string kind)
    {
        var characters = new List<char>(kind.Length + 4);
        for (var index = 0; index < kind.Length; index++)
        {
            var character = kind[index];
            if (index > 0 && char.IsUpper(character))
            {
                characters.Add(' ');
                character = char.ToLowerInvariant(character);
            }

            characters.Add(character);
        }

        return new string(characters.ToArray());
    }

    [HttpPut("evidence/{evidenceId:guid}/automation-approval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAutomationApproval(
        Guid evidenceId,
        [FromBody] AutomationApprovalRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var analysis = await database.WorkEvidenceAnalyses
            .SingleOrDefaultAsync(item => item.WorkEvidenceId == evidenceId, cancellationToken);

        if (analysis is null)
        {
            return NotFound();
        }

        if (request.Approved)
        {
            if (string.IsNullOrWhiteSpace(analysis.AutomationOpportunity) ||
                analysis.EstimatedTimeFreedMinutes is not > 0)
            {
                return BadRequest("This analysis has no measurable automation opportunity to approve.");
            }

            if (!analysis.AutomationApprovedAtUtc.HasValue)
            {
                analysis.AutomationApprovedAtUtc = DateTimeOffset.UtcNow;
                analysis.ApprovedTimeFreedMinutes = analysis.EstimatedTimeFreedMinutes;
            }
        }
        else
        {
            analysis.AutomationApprovedAtUtc = null;
            analysis.ApprovedTimeFreedMinutes = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string GetDisplayName(HumanWorkDimension dimension) =>
        dimension switch
        {
            HumanWorkDimension.StrategicReasoning => "Strategic reasoning",
            HumanWorkDimension.EmpathyAndCommunication => "Empathy & communication",
            HumanWorkDimension.LeadershipAndMentorship => "Leadership & mentorship",
            HumanWorkDimension.CreativeProblemSolving => "Creative problem solving",
            HumanWorkDimension.HelpAndIssueResolution => "Help & issue resolution",
            _ => dimension.ToString()
        };
}
