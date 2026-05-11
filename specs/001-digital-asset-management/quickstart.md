# Quickstart: Digital Asset Management System (adam)

## Prerequisites

- .NET 10 SDK
- For PostgreSQL provider: PostgreSQL 14+
- For SQL Server provider: SQL Server 2019+
- For multi-user integration tests: Docker (Testcontainers)

## Setup

```bash
git clone <repo-url> adam
cd adam
dotnet restore
dotnet build
```

## Run — Standalone Mode (no service required)

```bash
dotnet run --project src/Adam.CatalogBrowser -- --mode standalone --db "adam.db"
```

Launches the catalog browser as a self-contained desktop app with a local SQLite database. No broker service needed.

## Run — Multi-User Mode

### 1. Start the broker service

```bash
# SQLite (development)
dotnet run --project src/Adam.BrokerService -- --provider sqlite --connection "Data Source=adam.db"

# PostgreSQL (production)
dotnet run --project src/Adam.BrokerService -- --provider postgresql --connection "Host=localhost;Database=adam;Username=adam;Password=adam"

# SQL Server (enterprise)
dotnet run --project src/Adam.BrokerService -- --provider sqlserver --connection "Server=localhost;Database=adam;Trusted_Connection=True;TrustServerCertificate=True"
```

### 2. Launch the catalog browser

```bash
dotnet run --project src/Adam.CatalogBrowser -- --mode multiuser --endpoint "tcp://localhost:5000"
```

## Deploy as Native Service (Multi-User)

```bash
# Windows — register as Windows Service
dotnet run --project src/Adam.BrokerService -- --install

# macOS — register as launchd daemon
sudo dotnet run --project src/Adam.BrokerService -- --install

# Linux — register as systemd unit
sudo dotnet run --project src/Adam.BrokerService -- --install
```

## Migrate Standalone DB to Multi-User

Open the admin panel in the catalog browser and use the Database Migration Wizard to transfer a standalone SQLite database to PostgreSQL or SQL Server.

## Run Tests

```bash
# All tests
dotnet test

# Provider-specific integration tests (requires Docker)
dotnet test --filter "Category=Integration"
```

## Default Credentials (Multi-User)

| Username | Password | Role |
|----------|----------|------|
| admin | admin123 | Administrator |

*Change immediately in production.*
