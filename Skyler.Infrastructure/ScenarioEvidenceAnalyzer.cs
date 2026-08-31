using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class ScenarioEvidenceAnalyzer : IWorkEvidenceAnalyzer
{
    public Task<WorkEvidenceAnalysis> AnalyzeAsync(
        WorkEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (evidence.IsAbsence)
        {
            return Task.FromResult(CreateAbsenceAnalysis(evidence));
        }

        var searchableText = $"{evidence.Subject} {evidence.Content}";
        var automationOpportunity = CreateAutomationOpportunity(evidence, searchableText);
        var analysisId = Guid.NewGuid();
        var dimensions = new List<DimensionAssessment>
        {
            Assess(analysisId, HumanWorkDimension.StrategicReasoning, searchableText,
                "goal", "action item", "long-term", "priority", "strategy", "plan"),
            Assess(analysisId, HumanWorkDimension.EmpathyAndCommunication, searchableText,
                "help", "support", "thank", "listen", "understand", "check in"),
            Assess(analysisId, HumanWorkDimension.LeadershipAndMentorship, searchableText,
                "mentor", "coach", "guide", "teach", "skill", "feedback"),
            Assess(analysisId, HumanWorkDimension.CreativeProblemSolving, searchableText,
                "automate", "experiment", "alternative", "prototype", "creative", "improve"),
            Assess(analysisId, HumanWorkDimension.HelpAndIssueResolution, searchableText,
                "resolved", "solved", "issue gone", "root cause", "fixed", "unblocked")
        };

        return Task.FromResult(new WorkEvidenceAnalysis
        {
            Id = analysisId,
            WorkEvidenceId = evidence.Id,
            Analyzer = "Scenario analysis",
            UsedLocalModel = false,
            Summary = CreateSummary(evidence),
            InferredRole = null,
            RoleConfidence = 0,
            RoleRationale = "Synthetic scenario evidence is not used to infer the Outlook mailbox owner's role.",
            AutomationOpportunity = automationOpportunity,
            EstimatedTimeFreedMinutes = EstimateAutomationSavings(evidence, automationOpportunity),
            AnalysisVersion = WorkEvidenceAnalysis.CurrentAnalysisVersion,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            Dimensions = dimensions
        });
    }

    private static DimensionAssessment Assess(
        Guid analysisId,
        HumanWorkDimension dimension,
        string text,
        params string[] indicators)
    {
        var matches = indicators
            .Where(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DimensionAssessment
        {
            Id = Guid.NewGuid(),
            WorkEvidenceAnalysisId = analysisId,
            Dimension = dimension,
            Score = matches.Count == 0 ? 0 : Math.Min(90, 52 + (matches.Count * 9)),
            Confidence = matches.Count == 0 ? 0.5 : Math.Min(0.9, 0.5 + (matches.Count * 0.1)),
            Rationale = matches.Count == 0
                ? "No observable indicator for this dimension appears in the activity."
                : $"Scenario indicators detected: {string.Join(", ", matches)}."
        };
    }

    private static string CreateSummary(WorkEvidence evidence)
    {
        var origin = evidence.IsSynthetic ? "Synthetic Outlook" : "Outlook";
        return evidence.Kind == EvidenceKind.Email
            ? $"{origin} email contains observable work-enrichment signals."
            : $"{origin} meeting contains observable mentorship and communication signals.";
    }

    private static string? CreateAutomationOpportunity(WorkEvidence evidence, string text)
    {
        if (evidence.Kind != EvidenceKind.Email)
        {
            return null;
        }

        if (ContainsAny(text, "status", "report"))
        {
            return "Prepare the next status-report draft from the available Outlook evidence, leaving final review and sending to the employee.";
        }

        if (ContainsAny(text, "action item", "handoff", "follow-up"))
        {
            return "Extract action items and prepare a follow-up draft from the Outlook thread, leaving final review and sending to the employee.";
        }

        return ContainsAny(text, "weekly", "recurring", "data collection", "draft")
            ? "Prepare the repeatable first draft from the available Outlook evidence, leaving final review and any external action to the employee."
            : null;
    }

    private static bool ContainsAny(string text, params string[] indicators) =>
        indicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase));

    private static int? EstimateAutomationSavings(
        WorkEvidence evidence,
        string? automationOpportunity)
    {
        if (automationOpportunity is null)
        {
            return 0;
        }

        var currentEmployeeMinutes = evidence.ActualMinutes
            ?? evidence.BaselineMinutes
            ?? evidence.DurationMinutes
            ?? 15;

        return Math.Clamp((int)Math.Round(currentEmployeeMinutes * 0.75), 5, 120);
    }

    private static WorkEvidenceAnalysis CreateAbsenceAnalysis(WorkEvidence evidence)
    {
        var analysisId = Guid.NewGuid();
        var dimensions = Enum.GetValues<HumanWorkDimension>()
            .Select(dimension => new DimensionAssessment
            {
                Id = Guid.NewGuid(),
                WorkEvidenceAnalysisId = analysisId,
                Dimension = dimension,
                Score = null,
                Confidence = 0,
                Rationale = "Employee absence was explicitly recorded in Outlook."
            })
            .ToList();

        return new WorkEvidenceAnalysis
        {
            Id = analysisId,
            WorkEvidenceId = evidence.Id,
            Analyzer = "Outlook absence signal",
            UsedLocalModel = false,
            Summary = "Outlook explicitly marks the employee as absent for this activity.",
            InferredRole = null,
            RoleConfidence = 0,
            RoleRationale = "An absence record contains no evidence about the mailbox owner's functional role.",
            AutomationOpportunity = null,
            EstimatedTimeFreedMinutes = null,
            AnalysisVersion = WorkEvidenceAnalysis.CurrentAnalysisVersion,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            Dimensions = dimensions
        };
    }
}
