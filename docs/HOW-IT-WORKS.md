# How Skyler works

## What the app does

Skyler reads observable work from an authorized Outlook account and answers four questions:

1. What work is visible in recent sent email and calendar activity?
2. Which human-work strengths are demonstrated by that evidence?
3. What next actions and bounded automation opportunities are supported by the evidence?
4. How much time is associated with automation opportunities the employee has explicitly approved?

The app is evidence-based: each dashboard conclusion links back to a specific sent email or calendar event. The local model is instructed to avoid inferring work that is not present in the item.

## End-to-end workflow

```text
Authorized Outlook account
          |
          | sent email + calendar events
          v
Microsoft Graph evidence import
          |
          | normalized WorkEvidence records
          v
SQLite database
          |
          | new records without an analysis
          v
Local Ollama analysis + schema validation
          |
          | summaries, role evidence, dimensions,
          | suggested actions, automation proposals
          v
Dashboard API -> Portal -> employee review/approval
```

### 1. Outlook authorization

On the first API or standalone Worker run, Skyler uses Microsoft Authentication Library device-code flow. The console displays a verification URL and one-time code. The signed-in account must exactly match `Outlook:Mailbox`; Skyler rejects a token for a different account.

The delegated scopes are:

- `User.Read`
- `Mail.Read`
- `Calendars.Read`

The token cache is stored under `%LOCALAPPDATA%\Skyler\Authentication\outlook-msal-cache.bin`, allowing later runs to authenticate silently until Microsoft requires renewed consent or sign-in.

### 2. Evidence synchronization

The background worker runs once at startup and then every five minutes. The portal's Refresh button also starts the same Outlook synchronization immediately; it does not wait for model inference to finish.

For each cycle it imports:

- The most recent sent messages, up to `Outlook:MaxItems` and then filtered to `Outlook:SyncDays`.
- Calendar events from the synchronization window through one day in the future, up to `Outlook:MaxItems`.

For sent messages, Skyler stores the subject, unique body where available, recipients, and sent time. For meetings, it stores the subject, body/notes, attendees and organizer, start time, and duration.

Stable Graph IDs are converted into deterministic Skyler IDs. A unique `(Source, ExternalId)` database index prevents duplicate imports.

Analysis preservation is an invariant shared by automatic and manual synchronization:

- New Outlook items are inserted and queued for analysis.
- Existing Outlook evidence may receive updated source metadata, but its prior analysis is never deleted, replaced, or rerun.
- Only evidence that has never received an analysis enters the model queue.
- This applies to sent email, Outlook calendar events, Teams-linked meeting invitations, and every other model-analyzed evidence record.

This deliberately favors stable historical results over automatic reinterpretation. If an existing Outlook item's content changes after analysis, its stored analysis remains the original one; any future explicit reanalysis workflow must be a separate, deliberate operation.

Sent messages and calendar events are tagged as:

- **Mentorship** when their text contains configured mentorship indicators or a configured meeting-link signature. The checked-in settings recognize mentoring/coaching language plus personal and Microsoft 365 Teams meeting URLs. The lists are editable through `Outlook:MentorshipIndicators` and `Outlook:MentorshipMeetingLinkIndicators`.
- **Absence** when Outlook marks the event out-of-office, or an all-day event contains recognized leave/absence language.

These tags are rules-based metadata, separate from the local-model score.

### 3. Local analysis

Ordinary live Outlook evidence is sent to the configured Ollama `/api/chat` endpoint. Before inference, Skyler checks `/api/tags` to confirm the endpoint is available.

The analysis request includes:

- An embedded system prompt.
- An embedded neutral work taxonomy.
- Provenance rules for sent email versus calendar evidence.
- The normalized Outlook item.
- A strict JSON output schema.

The checked-in defaults use deterministic, conservative generation settings: temperature `0.1`, seed `42`, and an 8,192-token context.

Skyler validates the model response before saving it. A valid result must include:

- A `decided` or `undecided` evidence decision.
- A summary and suggested action.
- A decided or undecided functional-role assessment.
- Exactly one assessment for each of the five human-work dimensions.
- A bounded automation proposal and positive time estimate, or no proposal and a zero estimate.

Invalid, timed-out, or unreachable local-model results remain pending and are retried on a later worker pass. Already analyzed evidence is never placed back into this queue. Live evidence does not silently fall back to keyword scoring.

Absence records skip the local model and receive an explicit unscored absence analysis. The repository also contains a rules-based analyzer and synthetic scenario source for development scenarios, but the configured evidence source currently always uses live Microsoft Graph data.

### 4. Human-work dimensions

Every decided observation is scored from 0 to 100 in five dimensions:

| Dimension | Meaning in the dashboard |
|---|---|
| Strategic reasoning | Observable prioritization, planning, tradeoffs, or longer-term reasoning |
| Empathy and communication | Observable understanding, support, listening, or audience-aware communication |
| Leadership and mentorship | Observable coaching, guidance, feedback, development, or leadership ownership |
| Creative problem solving | Observable experimentation, alternatives, invention, or process improvement |
| Help and issue resolution | Observable diagnosis, resolution, unblocking, or root-cause work |

An undecided observation has `null` scores and zero confidence rather than forced low scores. The dashboard averages decided evidence within the selected period.

### 5. Functional role

Each live analysis may identify a functional role when the item contains enough role-distinguishing responsibility. The dashboard groups role decisions across all current, non-absence Outlook analyses—not only the selected day/week/month—and selects the role with the largest summed confidence.

If there is not enough evidence, the role remains **Undecided**.

### 6. Automation approval

The model may propose automation only for a bounded, repeatable task visible in the Outlook item. The proposal is stored with an estimated number of minutes that could be saved.

Until the employee approves it:

- It appears in the pending review queue.
- It contributes zero minutes to **Work handled for you**.

When the employee selects **Approve**:

1. The portal sends a `PUT` request for that evidence item.
2. The API records the approval timestamp.
3. The API copies the current estimated saving into the approved-time field.
4. The dashboard refreshes and includes the approved minutes in its AI-work history and totals.

Selecting **Approved** again revokes the approval and removes those minutes from the total.

Approval currently records the employee's decision; no external workflow, email, or automation is executed.

## Dashboard views

### Dashboard

The landing view summarizes:

- The inferred functional role.
- The strongest human-work signal in the selected period.
- Approved time savings and their share of the work-period baseline.
- The number of decided observations and automation opportunities.
- The full human-signal profile.
- Recently approved AI-work proposals.

### Work

The Work view derives two concise lists from analyzed activity:

- **Work to do** uses suggested actions from analyzed sent email.
- **What you accomplished** uses analysis summaries from non-absence evidence.

These are derived views of Outlook evidence; they are not a task-management database.

### Recent analysis

The Recent view lists every analyzed item in the selected period. Selecting an item shows:

- Source and timestamp.
- Analysis summary and analyzer identity.
- Each dimension score, confidence, and rationale.
- Suggested next action.
- Automation proposal and approval control, when applicable.

### Approvals

The Approvals view contains the current period's pending automation proposals and a history of approvals whose approval timestamp falls in the selected period.

### Settings

Settings displays the configured mailbox and stores the selected light, dark, or system theme in browser `localStorage` under `skyler-theme`.

## Periods and metrics

All API period boundaries are calculated in UTC.

| Selection | API period |
|---|---|
| Today | Start of the current UTC day through now |
| 7 days | Start of the UTC day six days ago through now |
| Month | Start of the current UTC month through now |

The main calculations are:

- **Dimension percentage:** average score for that dimension across decided, non-absence analyses in the period.
- **Decided observations:** analyses containing all five non-null dimension scores.
- **Mentorship time:** sum of durations for rule-tagged mentorship calendar events whose scheduled start is within the period and is not in the future. A Teams-link email can be tagged as mentorship but contributes no minutes because email has no meeting duration.
- **Period baseline:** number of UTC weekdays in the period × 480 minutes.
- **Time freed:** sum of estimates for approvals whose approval timestamps fall in the period.
- **Time-freed percentage:** approved minutes ÷ period baseline minutes × 100.

Synthetic evidence is excluded from the live dashboard. Absence records may appear as observations, but they do not contribute scores, role evidence, automation opportunities, or work accomplishments.

## Refresh and consistency

The browser loads the dashboard on mount, when the period changes, and after an approval change. Selecting Refresh performs an immediate Microsoft Graph synchronization, queues analysis for newly imported records in the background, and then reads the latest committed dashboard state. It does not block the browser while Ollama analyzes new evidence.

Automatic synchronization runs at startup and every five minutes. Automatic and manual synchronization call the same import logic and preserve all prior analyses. A scheduled Teams/Outlook calendar meeting is imported in advance but does not affect meeting or mentorship metrics until its scheduled start time. Starting a Teams call early does not change the Outlook calendar event's scheduled start.

If evidence is visible in Outlook but not in Skyler yet, select Refresh and check the API console for device-code authentication, Graph, Ollama, validation, or database messages. Newly synchronized evidence may take longer to appear in analysis-driven views while its first model analysis runs in the background.

## Privacy boundary

The default architecture is local-first, but it still connects to Microsoft Graph to read Outlook data.

- Raw email/calendar content, participants, and analysis results are stored in the local SQLite database.
- The configured Ollama URL defaults to localhost. Changing `LocalLlm:BaseUrl` to a remote host sends evidence to that host.
- Microsoft tokens are cached in the current Windows user's local application-data directory.
- The portal receives subjects, summaries, rationales, suggested actions, and approval data from the API; it does not receive the stored raw Outlook body in the current dashboard DTO.

The current API does not enforce application-level user authentication. It should be treated as a local development application until authentication, authorization, encrypted storage, and production secret management are added.
