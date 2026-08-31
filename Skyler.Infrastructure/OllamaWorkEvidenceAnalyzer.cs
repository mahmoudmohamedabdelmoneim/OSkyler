using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class OllamaWorkEvidenceAnalyzer(
    HttpClient httpClient,
    LocalLlmOptions options) : IWorkEvidenceAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonElement OutputSchema = ParseOutputSchema();

    public async Task<WorkEvidenceAnalysis> AnalyzeAsync(
        WorkEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(cancellationToken);

        var request = new OllamaChatRequest(
            options.Model,
            [
                new OllamaMessage("system", WorkAnalysisPromptContext.Value),
                new OllamaMessage("user", BuildEvidencePrompt(evidence))
            ],
            Stream: false,
            Format: OutputSchema,
            Options: new OllamaGenerationOptions(
                Temperature: 0.1,
                Seed: 42,
                NumCtx: 8192));

        using var inferenceTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inferenceTimeout.CancelAfter(TimeSpan.FromSeconds(options.InferenceTimeoutSeconds));

        using var response = await httpClient.PostAsJsonAsync(
            "api/chat",
            request,
            JsonOptions,
            inferenceTimeout.Token);
        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            JsonOptions,
            inferenceTimeout.Token);
        var content = ollamaResponse?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidDataException("The local model returned no analysis content.");
        }

        var payload = JsonSerializer.Deserialize<AnalysisPayload>(content, JsonOptions)
            ?? throw new InvalidDataException("The local model returned invalid analysis JSON.");

        return MapAndValidate(payload, evidence);
    }

    private async Task VerifyEndpointAsync(CancellationToken cancellationToken)
    {
        using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        healthTimeout.CancelAfter(TimeSpan.FromSeconds(options.HealthTimeoutSeconds));

        using var response = await httpClient.GetAsync("api/tags", healthTimeout.Token);
        response.EnsureSuccessStatusCode();
    }

    private WorkEvidenceAnalysis MapAndValidate(AnalysisPayload payload, WorkEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(payload.Summary))
        {
            throw new InvalidDataException("The local model omitted a required analysis field.");
        }

        var isDecided = payload.Decision?.Trim().ToLowerInvariant() switch
        {
            "decided" => true,
            "undecided" => false,
            _ => throw new InvalidDataException(
                "The local model must return a 'decided' or 'undecided' decision.")
        };
        var payloadDimensions = payload.Dimensions ?? [];
        var dimensions = new List<DimensionAssessment>();

        foreach (var dimension in Enum.GetValues<HumanWorkDimension>())
        {
            var matches = payloadDimensions
                .Where(item => string.Equals(
                    item.Dimension,
                    dimension.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count > 1)
            {
                throw new InvalidDataException($"The local model returned duplicate '{dimension}' dimensions.");
            }

            var match = matches.SingleOrDefault();
            ValidateAssessment(match, dimension, isDecided);
            var validatedAssessment = match!;

            dimensions.Add(new DimensionAssessment
            {
                Id = Guid.NewGuid(),
                Dimension = dimension,
                Score = isDecided ? validatedAssessment.Score : null,
                Confidence = isDecided ? validatedAssessment.Confidence : 0,
                Rationale = Limit(
                    validatedAssessment.Rationale ?? "No observable signal for this dimension in the activity.",
                    1000)
            });
        }

        var analysisId = Guid.NewGuid();
        foreach (var dimension in dimensions)
        {
            dimension.WorkEvidenceAnalysisId = analysisId;
        }

        var automationOpportunity = !isDecided || string.IsNullOrWhiteSpace(payload.AutomationOpportunity)
            ? null
            : Limit(payload.AutomationOpportunity.Trim(), 2000);

        var estimatedTimeFreedMinutes = NormalizeAutomationEstimate(
            automationOpportunity,
            payload.EstimatedTimeFreedMinutes,
            evidence,
            isDecided);
        var roleAssessment = MapAndValidateRole(payload.RoleAssessment);

        return new WorkEvidenceAnalysis
        {
            Id = analysisId,
            WorkEvidenceId = evidence.Id,
            Analyzer = $"Ollama / {options.Model}",
            UsedLocalModel = true,
            Summary = Limit(payload.Summary, 2000),
            InferredRole = roleAssessment.Title,
            RoleConfidence = roleAssessment.Confidence,
            RoleRationale = roleAssessment.Rationale,
            AutomationOpportunity = automationOpportunity,
            EstimatedTimeFreedMinutes = estimatedTimeFreedMinutes,
            AnalysisVersion = WorkEvidenceAnalysis.CurrentAnalysisVersion,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            Dimensions = dimensions
        };
    }

    private static RoleAssessment MapAndValidateRole(RoleAssessmentPayload? assessment)
    {
        if (assessment is null || string.IsNullOrWhiteSpace(assessment.Rationale))
        {
            throw new InvalidDataException("The local model omitted the functional-role assessment.");
        }

        if (assessment.Confidence is < 0 or > 1)
        {
            throw new InvalidDataException("The functional-role confidence must be from 0 to 1.");
        }

        var decision = assessment.Decision?.Trim().ToLowerInvariant();
        if (decision is not "decided" and not "undecided")
        {
            throw new InvalidDataException(
                "The functional-role decision must be 'decided' or 'undecided'.");
        }

        return decision switch
        {
            "decided" when !string.IsNullOrWhiteSpace(assessment.Title) && assessment.Confidence > 0 =>
                new RoleAssessment(
                    Limit(assessment.Title.Trim(), 200),
                    assessment.Confidence,
                    Limit(assessment.Rationale.Trim(), 1000)),
            _ => new RoleAssessment(
                null,
                0,
                Limit(assessment.Rationale.Trim(), 1000))
        };
    }

    private static void ValidateAssessment(
        DimensionPayload? assessment,
        HumanWorkDimension dimension,
        bool isDecided)
    {
        if (assessment is null)
        {
            throw new InvalidDataException($"The local model omitted the '{dimension}' dimension.");
        }

        if (isDecided && (assessment.Score is null or < 0 or > 100))
        {
            throw new InvalidDataException($"The '{dimension}' percentage must be from 0 to 100.");
        }

        if (assessment.Confidence is < 0 or > 1)
        {
            throw new InvalidDataException($"The '{dimension}' confidence must be from 0 to 1.");
        }

        if (string.IsNullOrWhiteSpace(assessment.Rationale))
        {
            throw new InvalidDataException($"The '{dimension}' rationale is required.");
        }
    }

    private static string BuildEvidencePrompt(WorkEvidence evidence)
    {
        var provenance = evidence.Kind == EvidenceKind.Email
            ? "Retrieved from the authorized mailbox owner's Sent Items. Attribute first-person statements in the email to the mailbox owner, but do not attribute quoted or reported actions by other people."
            : "Retrieved from the authorized mailbox owner's calendar. Attribute an action to the mailbox owner only when the notes explicitly identify that person as performing or owning it.";

        return $"""
            Use the following Outlook item as the only evidence for this decision.
            Application-supplied provenance: {provenance}
            <outlook-evidence authorized-mailbox="{evidence.EmployeeId}">
              Source: {evidence.Source}
              Kind: {evidence.Kind}
              Subject: {evidence.Subject}
              Participants: {evidence.Participants}
              Duration minutes: {evidence.DurationMinutes?.ToString() ?? "not supplied"}
              Baseline minutes: {evidence.BaselineMinutes?.ToString() ?? "not supplied"}
              Actual minutes: {evidence.ActualMinutes?.ToString() ?? "not supplied"}
              Employee absence: {(evidence.IsAbsence ? "yes" : "no")}
              Content: {evidence.Content}
            </outlook-evidence>
            """;
    }

    private static int? NormalizeAutomationEstimate(
        string? automationOpportunity,
        int? estimate,
        WorkEvidence evidence,
        bool isDecided)
    {
        if (evidence.IsAbsence)
        {
            return null;
        }

        if (!isDecided)
        {
            return 0;
        }

        if (automationOpportunity is null)
        {
            if (estimate is > 0)
            {
                throw new InvalidDataException(
                    "The local model estimated automation savings without describing an automation opportunity.");
            }

            return 0;
        }

        if (estimate is null or <= 0)
        {
            throw new InvalidDataException(
                "The local model supplied an automation opportunity without a positive time estimate.");
        }

        var suppliedBounds = new[]
            {
                evidence.ActualMinutes,
                evidence.BaselineMinutes,
                evidence.DurationMinutes
            }
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToList();
        var upperBound = suppliedBounds.Count == 0 ? 240 : suppliedBounds.Min();

        return Math.Clamp(estimate.Value, 1, upperBound);
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static JsonElement ParseOutputSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "decision": { "type": "string", "enum": ["decided", "undecided"] },
                "summary": { "type": "string", "minLength": 1 },
                "roleAssessment": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "decision": { "type": "string", "enum": ["decided", "undecided"] },
                    "title": { "type": ["string", "null"] },
                    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                    "rationale": { "type": "string", "minLength": 1 }
                  },
                  "required": ["decision", "title", "confidence", "rationale"]
                },
                "automationOpportunity": { "type": ["string", "null"] },
                "estimatedTimeFreedMinutes": { "type": "integer", "minimum": 0 },
                "dimensions": {
                  "type": "array",
                  "minItems": 5,
                  "maxItems": 5,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "dimension": {
                        "type": "string",
                        "enum": [
                          "StrategicReasoning",
                          "EmpathyAndCommunication",
                          "LeadershipAndMentorship",
                          "CreativeProblemSolving",
                          "HelpAndIssueResolution"
                        ]
                      },
                      "score": { "type": ["integer", "null"], "minimum": 0, "maximum": 100 },
                      "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                      "rationale": { "type": "string", "minLength": 1 }
                    },
                    "required": ["dimension", "score", "confidence", "rationale"]
                  }
                }
              },
              "required": [
                "decision",
                "summary",
                "roleAssessment",
                "automationOpportunity",
                "estimatedTimeFreedMinutes",
                "dimensions"
              ]
            }
            """);

        return document.RootElement.Clone();
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        bool Stream,
        JsonElement Format,
        OllamaGenerationOptions Options);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaGenerationOptions(
        double Temperature,
        int Seed,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed record OllamaChatResponse(OllamaMessage? Message);

    private sealed record AnalysisPayload(
        string? Decision,
        string? Summary,
        RoleAssessmentPayload? RoleAssessment,
        string? AutomationOpportunity,
        int? EstimatedTimeFreedMinutes,
        IReadOnlyList<DimensionPayload>? Dimensions);

    private sealed record DimensionPayload(
        string? Dimension,
        int? Score,
        double Confidence,
        string? Rationale);

    private sealed record RoleAssessmentPayload(
        string? Decision,
        string? Title,
        double Confidence,
        string? Rationale);

    private sealed record RoleAssessment(
        string? Title,
        double Confidence,
        string Rationale);
}
