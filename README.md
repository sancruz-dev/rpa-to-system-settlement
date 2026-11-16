# RPA to System: Settlement Reconciliation

Portfolio project that simulates taking over a legacy RPA automation (built with a desktop
automation tool) and turning it into an event-driven system on Azure.

## The scenario

A legacy robot opens a brokerage portal, downloads a CSV with the previous day's settlements,
compares it against the operations on record, and reports discrepancies by email. The robot is
brittle — it fails whenever the screen layout changes — and when it's retried, it sometimes
imports the same file twice. The new system removes the dependency on screen scraping and makes
every step idempotent and traceable.

**Status:** work in progress (phase 1 of 4).

## Structure

- `src/BrokeragePortal.Api` — a Minimal API (.NET 9) that simulates the brokerage portal: a fake
  login screen and a `GET /api/settlements/{date}` endpoint used both by the login screen's
  download button and, later, by the robot/system. The CSV is built from the operations table,
  with a fixed, deterministic pattern of 6 discrepancies on top (3 altered amounts, 2 missing
  rows, 1 extra row) to exercise the reconciliation logic.

Later phases (legacy desktop-automation robot, Azure Functions with automatic reconciliation,
tests and CI) will be documented here as development progresses.

## Running locally

Requires a local SQL Server instance with a database that has `Clients`, `Assets` and
`Operations` tables (client id/document, asset id/ticker, and operations with client, asset,
quantity, price and date). Point the app at it with a connection string named `SettlementDb`,
set via user secrets so it never ends up in source control:

```bash
cd src/BrokeragePortal.Api
dotnet user-secrets set "ConnectionStrings:SettlementDb" "Server=.;Database=YourDb;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls http://localhost:5196
```
