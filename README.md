# RPA to System: Settlement Reconciliation

Portfolio project that simulates taking over a legacy RPA automation (built with a desktop
automation tool) and turning it into an event-driven, idempotent system on Azure.

## The scenario

A legacy robot opens a brokerage portal, downloads a CSV with the previous day's settlements,
compares it against the operations on record, and reports discrepancies by email. The robot is
brittle — it fails whenever the screen layout changes — and when it's retried, it sometimes
imports the same file twice. The new system removes the dependency on screen scraping and makes
every step idempotent and traceable.

## Architecture

```
 ┌─────────────────────┐        ┌──────────────────────────────────┐
 │  Brokerage Portal    │        │        Azure Functions           │
 │  (ASP.NET Core API)  │        │                                   │
 │                      │◄───────┤  Timer Trigger                    │
 │  fake login +        │  HTTP  │  (daily) downloads the CSV,       │
 │  GET /api/settlements│        │  uploads it to Blob Storage,      │
 └──────────────────────┘        │  queues a message                │
                                  │           │                      │
                                  │           ▼                      │
                                  │  Queue Trigger                   │
                                  │  reads the blob, calls the       │
                                  │  idempotent file-registration    │
                                  │  procedure, imports the rows,    │
                                  │  reconciles and persists         │
                                  │  discrepancies                   │
                                  └───────────────┬───────────────────┘
                                                   ▼
                                         Azure SQL Database
                                 (clients, assets, operations, files,
                                  imported rows, discrepancies)
```

- **Legacy robot** (desktop automation, built manually by design): opens the portal, logs in,
  downloads the CSV, moves it to a working folder, and records the run's start/end by calling
  the same stored procedures the Azure Functions use. It does **not** check for duplicates —
  that fragility is exactly what the new system fixes.
- **Timer Trigger** (`DownloadSettlementsTimer`): calls the portal's own API (no screen
  scraping), uploads the CSV to Blob Storage and drops a message on a Storage Queue.
- **Queue Trigger** (`ProcessSettlementsQueue`): calls `uspRegistrarArquivo` — the same
  idempotent registration procedure the legacy robot calls — before importing anything. If the
  file was already registered, the function is a no-op: retries (a redelivered queue message, a
  re-run of the timer, a rerun of the whole pipeline) can never import the same file twice.
  It then imports the rows, runs the reconciliation query and persists the discrepancies.

### Why a queue instead of one single Function

The Timer and the processing step are split across a Storage Queue on purpose:
- **Decoupling.** The portal being slow or temporarily down only delays the queue message, it
  doesn't fail the whole pipeline.
- **Retry and poison-message handling come for free.** A failed processing attempt is retried
  automatically (default 5 times) and, if it keeps failing, moves to a poison queue instead of
  being silently dropped or looping forever — that's the dead-letter behavior the legacy email
  report never had.
- **Idempotency is what actually makes retries safe.** The queue gives you retries; the
  `UNIQUE` constraint on `NomeArquivo` plus `uspRegistrarArquivo`'s `UPDLOCK`/`HOLDLOCK`
  transaction is what makes a retry harmless instead of a duplicate import.

### Why Storage Queue (not Service Bus) for this phase

Storage Queue is free at this volume and — more importantly for a 1.5-day build — Azurite
emulates it faithfully for local development. Service Bus has no equivalent local emulator, so
building against it would mean testing the integration against the real cloud resource from the
very first run. Upgrading to Service Bus later (sessions, topics, finer-grained dead-lettering)
is a natural next step, not a redesign.

## Repository structure

- `src/BrokeragePortal.Api` — the simulated brokerage portal (fake login + the settlements API).
- `src/SettlementProcessing.Functions` — the Timer and Queue Trigger functions described above.
- `db/` — SQL scripts for objects this project adds on top of the pre-existing settlement
  schema (clients, assets, operations, file registration — already in place before this
  project started).
- `infra/main.bicep` — single resource group: Storage, Azure SQL Database (serverless, free
  usage limit, auto-pause), Function App (Consumption), a Free-tier Web App for the portal,
  Application Insights, and a Key Vault that both apps read their connection string from via
  their system-assigned managed identity. `infra/deploy.sh` / `infra/destroy.sh` wrap
  `az deployment group create|delete` for a one-command recreate/teardown.
- `Test/SettlementProcessing.Tests` — xUnit tests for the discrepancy pattern and CSV parsing.

## Current deployment status

The data layer is live on Azure: Storage account, Azure SQL Database (serverless, free usage
limit, auto-pause), Key Vault and Application Insights are deployed, and the database holds the
full schema and data (2,000 clients, 200,000 operations, the stored procedures and views).
**The Function App and the portal's Web App are not deployed yet** — the subscription used for
this project has its App Service quota (both Free and Consumption tiers) at zero in every region
tried, which blocks creating *any* new App Service Plan until Azure approves a quota increase
request. The Bicep template already defines both; deploying them is a matter of re-running
`infra/deploy.sh` once the quota clears.

Everything was verified end-to-end locally instead: Timer → Blob → Queue → idempotent import →
reconciliation, including a genuine duplicate-message retry that correctly did nothing the
second time.

## Running locally

**Portal:**
```bash
cd src/BrokeragePortal.Api
dotnet user-secrets set "ConnectionStrings:SettlementDb" "Server=.;Database=YourDb;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls http://localhost:5196
```

**Functions** (requires [Azurite](https://github.com/Azure/Azurite) running locally for Blob/Queue):
```bash
cd src/SettlementProcessing.Functions
func start
```
`local.settings.json` (gitignored) needs `SettlementDb`, `PortalBaseUrl` and the storage
settings — see the file's committed template for the exact keys.

**Tests:**
```bash
dotnet test Test/SettlementProcessing.Tests
```

Requires a SQL Server database with the `Clients`/`Assets`/`Operations`/`ArquivosLiquidacao`/
`LiquidacoesImportadas` schema and `uspRegistrarArquivo` already in place (see `db/` for the one
object this project adds on top of it).
