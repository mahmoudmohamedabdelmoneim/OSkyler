# OSkyler

OSkyler turns work activity into uniquely human work analysis dashboard. It reads work items and presents uniquely human-work signals and proposed automation opportunities for role/team review. Results are visible on spot.

The normal runtime has two application processes:

1. `Skyler.Api` imports and analyzes work evidence, owns the SQLite data, and exposes the dashboard API.
2. `Skyler.Portal` serves the generated Nuxt single-page application and proxies browser API calls to `Skyler.Api`.

`Skyler.Worker` is an alternative standalone host for the same ingestion pipeline. Do not normally run it alongside `Skyler.Api`, because both hosts register `OutlookAnalysisWorker` against the same database.

## Prerequisites

- .NET 10 SDK
- Node.js and npm
- Open model
- Microsoft Entra application configured properly
- Delegated Microsoft Graph permissions for read

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

The `OSkyler.slnLaunch` profile named **Skyler (API + Portal)** starts these same two projects from Visual Studio.

## Build behavior

`Skyler.Portal.csproj` runs `npm run generate` before every non-design-time build. Nuxt emits its static output into `Skyler.Portal/wwwroot`, and ASP.NET Core serves that output with an `index.html` fallback for the single-page application.

The frontend dependencies must therefore be installed before building the full .NET solution on a clean machine.

## Important Information

- Synchronization runs immediately at API startup and every five minutes afterward; the portal Refresh button triggers the same Graph synchronization on demand.
- Automatic and manual synchronization always preserve prior analyses for email, Outlook/Teams meetings, and all other model-analyzed evidence. Only new records without an analysis enter the model queue.
- Live work sync goes through model analysis before it appears in dashboard scoring.
- Raw work items and analyses are stored in `data/skyler.db` by default.
