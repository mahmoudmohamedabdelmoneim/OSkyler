# Skyler project architecture

## Architecture summary

Skyler is a .NET 10 solution with a Vue/Nuxt frontend, an ASP.NET Core API, a reusable infrastructure layer, and SQLite persistence. Microsoft Graph is the evidence source and an Ollama-compatible local model is the analysis engine.

The checked-in normal launch profile runs two processes:

```text
Browser
  |
  | http://localhost:5133
  v
Skyler.Portal (ASP.NET Core static host + /api reverse proxy)
  |
  | http://localhost:5128/api/*
  v
Skyler.Api (controllers + OutlookAnalysisWorker)
  |                         |
  |                         +--> Microsoft Graph / Outlook
  |                         +--> Ollama /api/tags and /api/chat
  v
data/skyler.db (SQLite)
```

`Skyler.Worker` can host the same `OutlookAnalysisWorker` without HTTP controllers. It is an alternative for separating background work from the API, but the API currently already hosts that worker. Running both with the same settings can cause duplicate processing attempts and is not part of the default topology.

## Solution projects

| Project | Type | Responsibility | Direct project dependencies |
|---|---|---|---|
| `Skyler.Core` | Class library | Domain entities, enums, and evidence/analyzer interfaces | None |
| `Skyler.Contracts` | Class library | Dashboard HTTP request/response records | None |
| `Skyler.Infrastructure` | Class library | EF Core/SQLite, database initialization, Graph authentication/import, Ollama analysis, scenario analysis, and background orchestration | `Skyler.Core` |
| `Skyler.Api` | ASP.NET Core Web API | Dependency composition, hosted analysis worker, dashboard query/approval endpoints, development OpenAPI | `Skyler.Contracts`, `Skyler.Core`, `Skyler.Infrastructure` |
| `Skyler.Portal` | ASP.NET Core static host | Builds/serves the Nuxt SPA and reverse-proxies `/api` | No .NET project references |
| `Skyler.Worker` | .NET Worker | Optional standalone host for the same synchronization and analysis pipeline | `Skyler.Core`, `Skyler.Infrastructure` |

The solution contains an empty `Skyler.Tests` solution folder, but no test project is currently present.

## Source layout

```text
Skyler/
|-- Skyler.Api/
|   |-- Controllers/DashboardController.cs
|   `-- Program.cs
|-- Skyler.Contracts/
|   `-- DashboardSummaryDto.cs
|-- Skyler.Core/
|   |-- WorkEvidence.cs
|   |-- WorkEvidenceAnalysis.cs
|   |-- DimensionAssessment.cs
|   |-- IWorkEvidenceSource.cs
|   `-- IWorkEvidenceAnalyzer.cs
|-- Skyler.Infrastructure/
|   |-- MicrosoftGraphOutlookEvidenceSource.cs
|   |-- OutlookTokenProvider.cs
|   |-- OutlookAnalysisWorker.cs
|   |-- OllamaWorkEvidenceAnalyzer.cs
|   |-- ScenarioEvidenceAnalyzer.cs
|   |-- SkylerDbContext.cs
|   |-- DatabaseInitializer.cs
|   |-- Prompts/
|   `-- ReferenceMaterials/
|-- Skyler.Portal/
|   |-- ClientApp/                 Nuxt source
|   |-- wwwroot/                   generated static output
|   `-- Program.cs                 static host and API proxy
|-- Skyler.Worker/
|   `-- Program.cs
|-- data/
|   `-- skyler.db                  runtime SQLite database
`-- OSkyler.slnx
```

The Razor component files still present under `Skyler.Portal/Components` and `Skyler.Portal/Services` are excluded by `Skyler.Portal.csproj`; the active frontend is `Skyler.Portal/ClientApp`.

## Dependency direction

The domain layer has no framework dependency. Infrastructure implements the abstractions defined by Core. The API is the composition root and maps domain/persistence data to Contracts.

```text
Skyler.Contracts <---------------- Skyler.Api ----------------> Skyler.Infrastructure
                                      |                                |
                                      v                                v
                                 Skyler.Core <-------------------- Skyler.Core

Skyler.Worker -------------------------------------------------> Skyler.Infrastructure
Skyler.Worker -------------------------------------------------> Skyler.Core

Skyler.Portal --HTTP only--> Skyler.Api
```

There is no compile-time dependency between the portal and the API contracts. The TypeScript interfaces in `ClientApp/app/types/dashboard.ts` mirror the C# response records manually.

## Runtime composition

### API host

`Skyler.Api/Program.cs` registers:

- MVC controllers and development OpenAPI.
- `SkylerDbContext` with SQLite.
- `LocalLlmOptions` and `OutlookOptions` from configuration.
- One long-lived `HttpClient` for Ollama.
- Graph token/evidence services.
- Local-model and scenario analyzers.
- `ResilientWorkEvidenceAnalyzer` as `IWorkEvidenceAnalyzer`.
- `ConfiguredOutlookEvidenceSource` as `IWorkEvidenceSource`.
- `OutlookAnalysisWorker` as a hosted service.

The database is initialized before the HTTP pipeline starts. HTTPS redirection is enabled outside Development. OpenAPI is exposed only in Development.

### Portal host

`Skyler.Portal/Program.cs` has two responsibilities:

1. Proxy every request under `/api` to `ApiBaseUrl`, preserving the HTTP method, query string, body, and most headers.
2. Serve generated files from `wwwroot`, default to `index.html`, and map unknown non-API paths back to `index.html` for SPA navigation.

The Nuxt application is configured with `ssr: false`. During frontend development, Nuxt's own development proxy targets `http://localhost:5128/api`.

### Standalone Worker host

`Skyler.Worker/Program.cs` registers the same database, Graph, analyzer, and hosted-worker services as the API but exposes no HTTP surface.

Use one of these topologies:

- **Current/default:** API hosts background processing; run API + Portal.
- **Future separated processing:** remove/disable the hosted worker registration in API, then run API + Worker + Portal.

Merely starting all three current projects does not create a clean separation because both API and Worker will process the same evidence database.

## Synchronization pipeline

`OutlookAnalysisWorker` owns the recurring pipeline:

1. Run synchronization immediately.
2. Analyze new, previously unanalyzed evidence.
3. Wait five minutes.
4. Repeat synchronization and analysis until shutdown.

`POST /api/dashboard/refresh` invokes the same synchronization routine used by the recurring cycle. The endpoint waits for Graph import only, queues analysis in the background, and returns `204 No Content`. Separate synchronization and analysis gates prevent overlapping work without making the browser wait for model inference.

### Import

`ConfiguredOutlookEvidenceSource` currently delegates unconditionally to `MicrosoftGraphOutlookEvidenceSource`. Although `OutlookOptions` contains a `Mode` value and a `ScenarioOutlookEvidenceSource` exists, mode-based source selection is not implemented.

The Graph source calls:

- `me/mailFolders/sentitems/messages` for sent email.
- `me/calendarView` for calendar events.

It requests plain-text bodies, applies length limits that match the entity model, and creates deterministic GUIDs from Graph IDs using SHA-256. The source imports at most 100 items per endpoint even if `MaxItems` is configured higher.

Synchronization loads existing evidence by `(Source, ExternalId)`:

- New items are inserted.
- Unchanged items are ignored.
- Changed live items receive updated source fields while their existing analysis is preserved.
- Synthetic items are not refreshed from source.

Mentorship detection is configuration-driven. `Outlook:MentorshipIndicators` contains semantic text markers, while `Outlook:MentorshipMeetingLinkIndicators` contains editable meeting-link signatures. Both sent email and calendar events use these lists, so Teams invitations can be tagged consistently regardless of which Graph collection supplied them.

Analysis preservation applies uniformly to every evidence kind, including email, Outlook meetings, Teams-linked invitations, and any record analyzed by the local model. Synchronization never deletes or replaces an existing `WorkEvidenceAnalysis`.

### Pending-analysis selection

Evidence is pending only when it has no analysis. Analysis-version changes, application restarts, source metadata changes, and synchronization mode do not enqueue an already analyzed record.

Pending items are analyzed sequentially. Each first analysis is written inside its own database transaction. Expected model/network/JSON failures are logged and remain pending for a future pass. Reanalysis, if introduced later, must be an explicit maintenance operation rather than a side effect of synchronization.

## Analyzer design

`ResilientWorkEvidenceAnalyzer` is a router rather than an exception fallback:

- Live, non-absence evidence -> `OllamaWorkEvidenceAnalyzer`.
- Synthetic or absence evidence -> `ScenarioEvidenceAnalyzer`.

### Ollama analyzer

`OllamaWorkEvidenceAnalyzer`:

1. Checks `GET /api/tags` with the health timeout.
2. Loads the embedded prompt and taxonomy once per process.
3. Sends `POST /api/chat` with `stream: false` and a strict JSON schema.
4. Deserializes and validates the response.
5. Maps it to one `WorkEvidenceAnalysis` and five `DimensionAssessment` records.

Validation prevents incomplete dimension sets, out-of-range scores/confidence, inconsistent automation estimates, or incomplete role decisions from entering the dashboard.

Automation estimates are capped by the smallest supplied positive value among actual minutes, baseline minutes, and duration minutes. If none is available, the cap is 240 minutes.

### Scenario analyzer

The rules-based analyzer handles explicit absence records without inferring work or savings. It can also score synthetic development scenarios by keyword, but current production composition does not register the synthetic evidence source.

## Domain model and persistence

### Active evidence model

```text
WorkEvidence
  1
  |
  | 0..1
  v
WorkEvidenceAnalysis
  1
  |
  | 0..5 (valid current analyses contain exactly five)
  v
DimensionAssessment
```

#### WorkEvidence

Represents one normalized Outlook item. Important fields include:

- Stable internal and external IDs.
- Mailbox/employee identifier.
- Source and kind (`Email` or `CalendarMeeting`).
- Subject, content, and participants.
- UTC occurrence time and optional duration/baseline/actual minutes.
- Mentorship, synthetic, and absence flags.

#### WorkEvidenceAnalysis

Stores one versioned analysis for an evidence item:

- Analyzer identity and whether a local model was used.
- Summary and suggested action.
- Functional-role decision, confidence, and rationale.
- Automation proposal, estimate, approval timestamp, and approved estimate snapshot.
- Analysis version/time and optional warning.

`TimeFreedMinutes` is computed in memory and is nonzero only after approval.

#### DimensionAssessment

Stores dimension, nullable score, confidence, and rationale. A unique index on `(WorkEvidenceAnalysisId, Dimension)` prevents duplicate dimensions.

### Legacy activity model

`WorkActivity`, its `WorkActivities` table, and `WorkActivityDto` remain in the solution but are not used by the current dashboard controller or frontend. They should be treated as legacy until removed or intentionally reintroduced.

### Database creation and schema updates

The connection string defaults to `Data Source=../data/skyler.db`. Relative paths are resolved from each host's content root, so both API and Worker resolve to the repository-level `data/skyler.db` in the standard layout.

The project does not currently use EF Core migration files. `EnsureCreatedSafelyAsync`:

1. Acquires an exclusive `<database>.init.lock` file for file-backed SQLite.
2. Calls `EnsureCreatedAsync`.
3. Uses SQLite `PRAGMA table_info` and targeted `ALTER TABLE` statements to add known historical columns.

The lock coordinates schema initialization, not the full synchronization/analysis pipeline.

## HTTP API

### `GET /api/dashboard?period=<day|week|month>`

Returns `DashboardSummaryDto`. Unknown period values normalize to `week`.

The query:

- Filters evidence to `Dashboard:Mailbox`.
- Excludes synthetic evidence.
- Requires the current analysis version.
- Requires live items to have used the local model, except explicit absence records.
- Filters observation/dimension results to the requested UTC period.
- Computes role from all current historical non-absence analyses.
- Computes approved savings from approval timestamps inside the requested period.

The response includes aggregate metrics, role, dimension averages, approved AI-work items, and detailed recent analyses. It intentionally does not include the raw stored email/calendar content.

### `PUT /api/dashboard/evidence/{evidenceId}/automation-approval`

Request body:

```json
{
  "approved": true
}
```

Returns:

- `204 No Content` after approval or revocation.
- `404 Not Found` when the analysis does not exist.
- `400 Bad Request` when approval is requested without a measurable automation proposal.

Approval snapshots the current estimate. Revocation clears the timestamp and approved estimate.

## Dashboard calculations

| Value | Calculation |
|---|---|
| Evidence count | Current analyzed observations in the selected period, including absence |
| Decided observations | Non-absence analyses with all five scores present |
| Dimension score | Mean score for the dimension across decided period analyses |
| Role | Highest summed-confidence role group across all historical current analyses |
| Mentorship minutes | Sum of durations for mentorship-tagged period events |
| Workday baseline | 480 minutes |
| Period baseline | UTC weekdays in the inclusive period × 480 |
| Time freed | Sum of approved estimate snapshots whose approval timestamps are in the period |
| Time-freed percentage | `time freed / period baseline × 100` |

Because savings are grouped by approval time, approving an older evidence item contributes to the period in which it was approved, not the period in which the Outlook item occurred.

## Frontend architecture

The Nuxt app is a single Vue component (`app/app.vue`) plus a global stylesheet and TypeScript response interfaces.

State is client-side and in-memory:

- `activeView`: dashboard, work/outlook, recent, approvals, or settings.
- `selectedPeriod`: day, week, or month.
- `summary`: most recent dashboard API response.
- `selectedEvidenceId`: detail selection.
- Loading, refresh, approval, error, and theme state.

There are no route-specific Vue pages or external state-management library. View changes replace conditional sections in the same component.

The theme preference is the only browser-persisted state. Approval state is persisted through the API to SQLite.

### Frontend build

`Skyler.Portal.csproj` excludes `ClientApp` and existing `wwwroot` files from default .NET item discovery, then runs the `GenerateClientApp` target before `PrepareForBuild`:

```text
npm run generate
  -> Nuxt static generation
  -> Skyler.Portal/wwwroot
  -> files re-added as ASP.NET Content
```

This makes the .NET portal artifact self-contained after frontend dependencies are installed.

## Configuration reference

### API and Worker

| Key | Purpose | Checked-in default |
|---|---|---|
| `ConnectionStrings:SkylerDatabase` | SQLite connection | `Data Source=../data/skyler.db` |
| `LocalLlm:BaseUrl` | Ollama-compatible base URL | `http://localhost:11434/` |
| `LocalLlm:Model` | Model passed to `/api/chat` | `mistral` |
| `LocalLlm:HealthTimeoutSeconds` | `/api/tags` timeout | `10` |
| `LocalLlm:InferenceTimeoutSeconds` | `/api/chat` timeout | `180` |
| `Outlook:Mode` | Reserved mode value; not currently used for source selection | `Live` |
| `Outlook:ClientId` | Microsoft public-client application ID | Repository-specific value |
| `Outlook:Mailbox` | Required authorized account and evidence owner ID | Repository-specific value |
| `Outlook:Authority` | MSAL authority | Microsoft consumer authority |
| `Outlook:SyncDays` | Lookback window | `30` |
| `Outlook:MaxItems` | Maximum items per Graph endpoint, clamped 1–100 | `50` |
| `Outlook:MentorshipIndicators` | Editable text fragments that tag email or meetings as mentorship | Mentoring, coaching, career-development, and one-on-one terms |
| `Outlook:MentorshipMeetingLinkIndicators` | Editable URL fragments that tag meeting invitations as mentorship | Personal Teams and Microsoft 365 Teams meeting links |

### API only

| Key | Purpose |
|---|---|
| `Dashboard:Mailbox` | Mailbox filter for dashboard queries; should match `Outlook:Mailbox` |

### Portal only

| Key | Purpose | Development value |
|---|---|---|
| `ApiBaseUrl` | Upstream API origin for the ASP.NET proxy | `http://localhost:5128/` |

For local credentials and account-specific values, prefer .NET user secrets or environment variables over adding new sensitive values to `appsettings.json`.

## Security and privacy considerations

The implementation is suitable for local development but is not a production security boundary yet:

- The API calls `UseAuthorization` but registers no authentication scheme and applies no authorization policy to the controller.
- `Dashboard:Mailbox` is a data filter, not proof of caller identity.
- SQLite contains raw Outlook bodies/notes and participant addresses without application-level encryption.
- The MSAL cache is persisted under the OS user profile.
- The default Ollama endpoint is local, but configuration can move model processing off-device.
- The portal proxy forwards client request headers to the API, but there is no user/session model.
- There are no automated tests in the solution.

Before production use, add authenticated user identity, per-user authorization, protected secrets, encrypted data-at-rest strategy, retention/deletion controls, audit logging, CSRF/replay considerations for mutations, and automated tests.

## Operational behavior and failure modes

| Condition | Current behavior |
|---|---|
| Microsoft sign-in required | Device-code instructions are logged to the host console |
| Wrong Microsoft account authorized | Token acquisition fails with an account-mismatch exception |
| Graph unavailable or rejects the request | Synchronization pass fails and host logs the exception |
| Ollama health check/inference times out | Evidence remains pending for a later pass |
| Model returns invalid JSON/schema content | Evidence remains pending for a later pass |
| Existing Outlook item changes | Stored evidence updates and old analysis is removed |
| Analysis version increases | Older analyses become pending automatically |
| API unavailable | Portal shows its dashboard load/approval error state |
| No scored evidence | Role/dimensions remain undecided or empty |

## Extension points

The existing abstractions support several clean extensions:

- Add another evidence provider by implementing `IWorkEvidenceSource`.
- Add mode-based source selection in `ConfiguredOutlookEvidenceSource`.
- Add another analysis engine by implementing `IWorkEvidenceAnalyzer`.
- Separate worker execution by removing API worker registration and deploying `Skyler.Worker` independently.
- Replace manual TypeScript contract mirroring with generated clients from the development OpenAPI document.
- Replace ad-hoc schema patching with versioned EF Core migrations.
- Split `app.vue` into route pages, composables, and focused view components as the frontend grows.

## Key implementation files

- [`Skyler.Api/Program.cs`](../Skyler.Api/Program.cs)
- [`Skyler.Api/Controllers/DashboardController.cs`](../Skyler.Api/Controllers/DashboardController.cs)
- [`Skyler.Infrastructure/OutlookAnalysisWorker.cs`](../Skyler.Infrastructure/OutlookAnalysisWorker.cs)
- [`Skyler.Infrastructure/MicrosoftGraphOutlookEvidenceSource.cs`](../Skyler.Infrastructure/MicrosoftGraphOutlookEvidenceSource.cs)
- [`Skyler.Infrastructure/OutlookTokenProvider.cs`](../Skyler.Infrastructure/OutlookTokenProvider.cs)
- [`Skyler.Infrastructure/OllamaWorkEvidenceAnalyzer.cs`](../Skyler.Infrastructure/OllamaWorkEvidenceAnalyzer.cs)
- [`Skyler.Infrastructure/SkylerDbContext.cs`](../Skyler.Infrastructure/SkylerDbContext.cs)
- [`Skyler.Portal/Program.cs`](../Skyler.Portal/Program.cs)
- [`Skyler.Portal/ClientApp/app/app.vue`](../Skyler.Portal/ClientApp/app/app.vue)
