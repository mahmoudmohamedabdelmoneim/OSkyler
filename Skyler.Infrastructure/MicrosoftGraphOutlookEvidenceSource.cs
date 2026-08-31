using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class MicrosoftGraphOutlookEvidenceSource(
    OutlookTokenProvider tokenProvider,
    OutlookOptions options) : IWorkEvidenceSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<IReadOnlyList<WorkEvidence>> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        var start = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.SyncDays));
        var end = DateTimeOffset.UtcNow.AddDays(1);

        var mail = await GetSentMailAsync(accessToken, start, cancellationToken);
        var meetings = await GetCalendarEventsAsync(accessToken, start, end, cancellationToken);

        return mail.Concat(meetings).ToList();
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<IReadOnlyList<WorkEvidence>> GetSentMailAsync(
        string accessToken,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        var path = "me/mailFolders/sentitems/messages" +
                   "?$select=id,subject,body,uniqueBody,bodyPreview,sentDateTime,toRecipients,ccRecipients" +
                   $"&$top={Math.Clamp(options.MaxItems, 1, 100)}" +
                   "&$orderby=sentDateTime desc";
        var response = await GetAsync<GraphCollection<GraphMessage>>(
            path,
            accessToken,
            preferUtc: false,
            cancellationToken);

        return response.Value
            .Where(message =>
                !string.IsNullOrWhiteSpace(message.Id) &&
                TryParseGraphDateTime(message.SentDateTime, out var sentAt) &&
                sentAt >= start)
            .Select(message =>
            {
                TryParseGraphDateTime(message.SentDateTime, out var sentAt);
                var externalId = $"graph-message:{message.Id}";
                var subject = Limit(message.Subject, 500, "(No subject)");
                var content = Limit(
                    message.UniqueBody?.Content ?? message.Body?.Content ?? message.BodyPreview,
                    8000,
                    "(No email body available)");
                return new WorkEvidence
                {
                    Id = CreateDeterministicGuid(externalId),
                    ExternalId = externalId,
                    EmployeeId = options.Mailbox,
                    Source = EvidenceSource.Outlook,
                    Kind = EvidenceKind.Email,
                    Subject = subject,
                    Content = content,
                    Participants = Limit(
                        FormatRecipients(
                            message.ToRecipients.Concat(message.CcRecipients)
                                .Select(recipient => recipient.EmailAddress)),
                        2000,
                        "(No recipients available)"),
                    OccurredAtUtc = sentAt,
                    IsMentorship = IsMentorship(subject, content),
                    IsSynthetic = false
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<WorkEvidence>> GetCalendarEventsAsync(
        string accessToken,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var path = "me/calendarView" +
                   $"?startDateTime={Uri.EscapeDataString(start.ToString("O"))}" +
                   $"&endDateTime={Uri.EscapeDataString(end.ToString("O"))}" +
                   "&$select=id,subject,body,bodyPreview,start,end,attendees,organizer,showAs,isAllDay,categories" +
                   $"&$top={Math.Clamp(options.MaxItems, 1, 100)}" +
                   "&$orderby=start/dateTime";
        var response = await GetAsync<GraphCollection<GraphEvent>>(
            path,
            accessToken,
            preferUtc: true,
            cancellationToken);

        return response.Value
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Id) &&
                TryParseGraphDateTime(item.Start?.DateTime, out _) &&
                TryParseGraphDateTime(item.End?.DateTime, out _))
            .Select(item =>
            {
                TryParseGraphDateTime(item.Start?.DateTime, out var startsAt);
                TryParseGraphDateTime(item.End?.DateTime, out var endsAt);
                var duration = Math.Max(0, (int)Math.Round((endsAt - startsAt).TotalMinutes));
                var externalId = $"graph-event:{item.Id}";
                var subject = Limit(item.Subject, 500, "(No meeting subject)");
                var content = Limit(
                    item.Body?.Content ?? item.BodyPreview,
                    8000,
                    "(No meeting notes available)");

                return new WorkEvidence
                {
                    Id = CreateDeterministicGuid(externalId),
                    ExternalId = externalId,
                    EmployeeId = options.Mailbox,
                    Source = EvidenceSource.Outlook,
                    Kind = EvidenceKind.CalendarMeeting,
                    Subject = subject,
                    Content = content,
                    Participants = Limit(
                        FormatRecipients(
                            item.Attendees.Select(attendee => attendee.EmailAddress)
                                .Append(item.Organizer?.EmailAddress)),
                        2000,
                        "(No participants available)"),
                    OccurredAtUtc = startsAt,
                    DurationMinutes = duration,
                    IsMentorship = IsMentorship(subject, content),
                    IsAbsence = IsAbsence(item, subject, content),
                    IsSynthetic = false
                };
            })
            .ToList();
    }

    private async Task<T> GetAsync<T>(
        string relativePath,
        string accessToken,
        bool preferUtc,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (preferUtc)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
        }
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "outlook.body-content-type=\"text\"");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Microsoft Graph returned {(int)response.StatusCode} ({response.ReasonPhrase}): {Limit(error, 500, "No details")}",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Microsoft Graph returned an empty response.");
    }

    private static string FormatRecipients(IEnumerable<GraphEmailAddress?> addresses) =>
        string.Join(
            "; ",
            addresses
                .Where(item => !string.IsNullOrWhiteSpace(item?.Address))
                .Select(item => item!.Address!)
                .Distinct(StringComparer.OrdinalIgnoreCase));

    private static bool TryParseGraphDateTime(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);

    private bool IsMentorship(string subject, string content)
    {
        var text = $"{subject} {content}";
        return options.MentorshipIndicators
            .Concat(options.MentorshipMeetingLinkIndicators)
            .Where(indicator => !string.IsNullOrWhiteSpace(indicator))
            .Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAbsence(GraphEvent item, string subject, string content)
    {
        if (string.Equals(item.ShowAs, "oof", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var text = $"{subject} {content} {string.Join(' ', item.Categories)}";
        string[] indicators =
        [
            "out of office", "ooo", "annual leave", "vacation", "sick leave",
            "personal leave", "employee absent"
        ];

        return item.IsAllDay && indicators.Any(indicator =>
            text.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Limit(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Replace('\uFFFD', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed class GraphCollection<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];
    }

    private sealed class GraphMessage
    {
        public string? Id { get; set; }
        public string? Subject { get; set; }
        public GraphItemBody? Body { get; set; }
        public GraphItemBody? UniqueBody { get; set; }
        public string? BodyPreview { get; set; }
        public string? SentDateTime { get; set; }
        public List<GraphRecipient> ToRecipients { get; set; } = [];
        public List<GraphRecipient> CcRecipients { get; set; } = [];
    }

    private sealed class GraphEvent
    {
        public string? Id { get; set; }
        public string? Subject { get; set; }
        public GraphItemBody? Body { get; set; }
        public string? BodyPreview { get; set; }
        public GraphDateTimeZone? Start { get; set; }
        public GraphDateTimeZone? End { get; set; }
        public List<GraphAttendee> Attendees { get; set; } = [];
        public GraphRecipient? Organizer { get; set; }
        public string ShowAs { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
        public List<string> Categories { get; set; } = [];
    }

    private sealed class GraphAttendee
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private sealed class GraphRecipient
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private sealed class GraphEmailAddress
    {
        public string? Address { get; set; }
    }

    private sealed class GraphDateTimeZone
    {
        public string? DateTime { get; set; }
    }

    private sealed class GraphItemBody
    {
        public string? ContentType { get; set; }
        public string? Content { get; set; }
    }
}
