# Skyler

Skyler turns recent Outlook activity into a private work-intelligence dashboard. It imports sent email and calendar events, analyzes observable work with a configured Ollama model, stores the results in SQLite, and presents human-work signals and proposed automation opportunities for employee review.

The normal local runtime has two application processes:

1. `Skyler.Api` imports and analyzes Outlook evidence, owns the SQLite data, and exposes the dashboard API.
2. `Skyler.Portal` serves the generated Nuxt single-page application and proxies browser API calls to `Skyler.Api`.

`Skyler.Worker` is an alternative standalone host for the same ingestion pipeline. Do not normally run it alongside `Skyler.Api`, because both hosts register `OutlookAnalysisWorker` against the same database.

## Documentation

- [How Skyler works](docs/HOW-IT-WORKS.md) — product behavior, dashboard views, metrics, approvals, and the end-to-end user workflow.
- [Project architecture](docs/ARCHITECTURE.md) — projects, runtime processes, dependencies, data flow, persistence, API contracts, configuration, and current limitations.

## Prerequisites

- .NET 10 SDK
- Node.js and npm
- Ollama running with the configured model; the repository defaults to `mistral`
- A Microsoft Entra application configured as a public client for device-code authentication
- Delegated Microsoft Graph permissions: `User.Read`, `Mail.Read`, and `Calendars.Read`

No client secret is used. On first startup, the API prints a Microsoft device-code sign-in instruction to its console.

## First-time setup

From the repository root:

```powershell
npm --prefix Skyler.Portal/ClientApp ci
ollama pull mistral
dotnet build OSkyler.slnx
```

Set the Outlook application/client ID and mailbox for the local environment. `Outlook:Mailbox` and `Dashboard:Mailbox` must identify the same account or the dashboard query will not find the imported evidence.

The API and Worker projects intentionally share a .NET user-secrets ID:

```powershell
dotnet user-secrets set --project Skyler.Api "Outlook:ClientId" "<public-client-application-id>"
dotnet user-secrets set --project Skyler.Api "Outlook:Mailbox" "<mailbox-address>"
dotnet user-secrets set --project Skyler.Api "Dashboard:Mailbox" "<mailbox-address>"
```

## Run locally

Ensure Ollama is running, then start the API and portal in separate terminals:

```powershell
dotnet run --project Skyler.Api --launch-profile http
```

```powershell
dotnet run --project Skyler.Portal --launch-profile http
```

Open [http://localhost:5133](http://localhost:5133). The API listens on `http://localhost:5128` with the checked-in development profiles.

The `OSkyler.slnLaunch` profile named **Skyler (API + Portal)** starts these same two projects from Visual Studio.

## Build behavior

`Skyler.Portal.csproj` runs `npm run generate` before every non-design-time build. Nuxt emits its static output into `Skyler.Portal/wwwroot`, and ASP.NET Core serves that output with an `index.html` fallback for the single-page application.

The frontend dependencies must therefore be installed before building the full .NET solution on a clean machine.

## Important current behavior

- Synchronization runs immediately at API startup and every five minutes afterward; the portal Refresh button triggers the same Graph synchronization on demand.
- Only sent email and calendar events are imported; inbox email is not read by the evidence source.
- Automatic and manual synchronization always preserve prior analyses for email, Outlook/Teams meetings, and all other model-analyzed evidence. Only new records without an analysis enter the model queue.
- Configured Teams meeting URLs in sent invitations or calendar bodies are tagged as mentorship. Calendar duration contributes only after the scheduled meeting start; an email link alone has no duration.
- Live Outlook evidence requires a successful local-model analysis before it appears in dashboard scoring.
- Approving an automation records the proposal and its estimated time saving. It does **not** execute an automation.
- Raw Outlook evidence and analyses are stored in `data/skyler.db` by default.
- The current API has no end-user authentication or authorization policy. Keep it on a trusted local interface until access control is implemented.
