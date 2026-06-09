# Adam Admin Guide

> **Version:** 1.0 — Covers the `Adam.ServiceManager` administration tool and `Adam.BrokerService` deployment.

This guide is for system administrators deploying and managing the Adam Digital Asset Management (DAM) system in a multi-user environment. If you are an end-user browsing assets, see the [User Guide](user-guide.md).

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Installing the Broker Service](#3-installing-the-broker-service)
   - [Windows (SCM)](#windows-scm)
   - [macOS (launchd)](#macos-launchd)
   - [Linux (systemd)](#linux-systemd)
   - [Port Configuration](#port-configuration)
   - [Firewall Configuration](#firewall-configuration)
4. [Service Manager](#4-service-manager)
   - [Launching & Elevation](#launching--elevation)
   - [Admin Panel Tab](#admin-panel-tab)
   - [Connection Endpoint](#connection-endpoint)
   - [TLS Configuration](#tls-configuration)
   - [Polling and Monitoring](#polling-and-monitoring)
5. [User Management](#5-user-management)
   - [Roles and Permissions](#roles-and-permissions)
   - [Adding Users](#adding-users)
   - [Editing Users](#editing-users)
   - [Deactivating Users](#deactivating-users)
6. [Database Configuration](#6-database-configuration)
   - [Supported Providers](#supported-providers)
   - [Migration Wizard (SQLite → PostgreSQL / SQL Server)](#migration-wizard)
   - [Manual Configuration](#manual-configuration)
7. [Audit Log](#7-audit-log)
   - [Logged Actions](#logged-actions)
   - [Filtering](#filtering)
   - [Retention](#retention)
8. [Connecting the Catalog Browser](#8-connecting-the-catalog-browser)
9. [Troubleshooting](#9-troubleshooting)
10. [Appendices](#10-appendices)

---

## 1. Overview

Adam operates in two modes:

| Mode | Description |
|------|-------------|
| **Standalone** | Single-user, direct SQLite access. No Broker service required. All permissions granted. |
| **Multi-User** | Multiple users connect to a shared database through the `BrokerService` TCP broker. JWT authentication, role-based access control (RBAC). |

The **Service Manager** (`Adam.ServiceManager`) is the administrative interface for multi-user mode. It provides:

- **Admin Panel** — Install/start/stop/uninstall the Broker service, configure host/port/TLS, view service logs
- **Users** — Add/edit/deactivate users, assign roles
- **Audit** — View and filter the audit log

The **Broker Service** (`Adam.BrokerService`) is a TCP server that:

- Authenticates users via JWT tokens
- Mediates all database access for connected clients
- Pushes change notifications for real-time updates
- Detects session invalidation (role changes, deactivation)

---

## 2. Architecture

```
┌─────────────────┐     TCP (protobuf)     ┌─────────────────┐
│  Catalog Browser │ ◄──────────────────►  │  BrokerService  │
│  (Client)        │     length-prefixed   │  (TCP Server)   │
└─────────────────┘     frames, 256MB max  └────────┬────────┘
                                                     │
                                           ┌─────────▼────────┐
                                           │  Shared Database  │
                                           │  (SQLite / PgSQL │
                                           │   / SQL Server)  │
                                           └──────────────────┘

┌─────────────────────┐
│   Service Manager   │──► Installs/manages BrokerService
│   (Administration)  │──► Manages users, roles, audit logs
└─────────────────────┘
```

**Key protocol details:**

- All communication uses **raw TCP** with length-prefixed protobuf framing (`TcpFrame`)
- Maximum payload size: **256 MB** (enforced by the server)
- Send timeout: **30 seconds**; receive timeout: **5 minutes**
- JWT tokens expire after a configurable period (default: **24 hours**)

---

## 3. Installing the Broker Service

The Broker Service can be installed as a platform-native service on all three supported operating systems. The Service Manager handles installation automatically, but you can also install manually.

### Prerequisites

- **.NET 10 runtime** installed on the server
- **Database** accessible (SQLite file, PostgreSQL server, or SQL Server)
- **Administrator/root privileges** for service installation

### Windows (SCM)

The Service Manager uses `sc.exe` under the hood via `WindowsServiceInstaller`.

**Automatic (recommended):**
1. Launch **Service Manager as Administrator** (right-click → *Run as administrator*)
2. Go to the **Admin Panel** tab
3. Set the desired **Port** (default: 9100)
4. Click **Install Service**
5. The installer checks port availability, creates the Windows service, adds a firewall rule, and starts the service

**Manual:**
```powershell
# From the publish directory, as Administrator:
sc.exe create "AdamBroker" binPath="C:\Adam\BrokerService\Adam.BrokerService.exe" start=auto
sc.exe start AdamBroker
```

**Note:** The port is configured via `appsettings.json` (`Broker:Port`), not a CLI argument. Update the config file before installing.

**Service details:**

| Property | Value |
|----------|-------|
| Service Name | `AdamBroker` |
| Display Name | Adam Broker Service |
| Startup Type | Automatic |
| Run As | LocalSystem |

### macOS (launchd)

The Service Manager uses `launchctl` via `MacOsServiceInstaller`. Launch the Service Manager and use the **Install Service** button on the Admin Panel.

**Manual plist location:** `~/Library/LaunchAgents/com.adam.broker.plist`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.adam.broker</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/share/adam/Adam.BrokerService</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
</dict>
</plist>
```

> **Note:** The port is configured via `appsettings.json` (`Broker:Port`), not a CLI argument. Update the config file in the same directory as the executable.

### Linux (systemd)

The Service Manager uses `systemctl` via `LinuxServiceInstaller`. Launch the Service Manager and use the **Install Service** button on the Admin Panel.

**Manual unit file:** `/etc/systemd/system/adam-broker.service`

```ini
[Unit]
Description=Adam Broker Service
After=network.target

[Service]
Type=simple
ExecStart=/usr/lib/adam/Adam.BrokerService
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

> **Note:** The port is configured via `appsettings.json` (`Broker:Port`), not a CLI argument. Ensure the config file is placed in the same directory as the executable.

```bash
sudo systemctl daemon-reload
sudo systemctl enable adam-broker
sudo systemctl start adam-broker
```

### Port Configuration

- **Default port:** 9100
- **Range:** 1–65535 (standard ephemeral range recommended: 49152–65535 for non-privileged users on Linux/macOS)
- **Port checker:** The Service Manager validates port availability before installation using `PortChecker.IsPortFree()`. If the port is in use, it recommends an alternative.

Configure the port in:
1. The Service Manager UI (**Admin Panel → Port** field)
2. Or directly in `appsettings.json` under the `Broker:Port` key:

```json
{
  "Broker": {
    "Port": 9100
  }
}
```

### Firewall Configuration

On **Windows**, the Service Manager automatically creates a Windows Firewall inbound rule named **"Adam Broker Service (TCP)"** during installation. The rule opens the configured port for inbound TCP traffic.

**Firewall rule details:**

| Property | Value |
|----------|-------|
| Rule Name | `Adam Broker Service (TCP)` |
| Protocol | TCP |
| Port | As configured (default 9100) |
| Direction | Inbound |
| Action | Allow |

The rule is removed when the service is uninstalled.

**Manual (Windows):**
```powershell
netsh advfirewall firewall add rule name="Adam Broker Service (TCP)" dir=in action=allow protocol=TCP localport=9100
```

**Manual (Linux — using ufw):**
```bash
sudo ufw allow 9100/tcp
```

**Manual (macOS):**
Go to **System Settings → Network → Firewall → Options** and add the Broker Service executable.

---

## 4. Service Manager

The Service Manager (`Adam.ServiceManager.exe`) is the administration GUI for the Adam DAM system.

### Launching & Elevation

- On **Windows**, always launch the Service Manager **as Administrator** to perform service management operations. The app detects elevation state and shows a **"Relaunch as Administrator"** button if not elevated.
- On **Linux/macOS**, run with `sudo` when performing service management.

```bash
# Linux
sudo dotnet /opt/adam/Adam.ServiceManager.dll

# macOS
sudo dotnet /Applications/Adam.app/Contents/MacOS/Adam.ServiceManager.dll
```

> **Note:** The Service Manager **requires** a running Broker Service for user management and audit log viewing. In standalone mode, these features read the local SQLite database directly.

### Admin Panel Tab

The Admin Panel tab displays:

- **Service Status** — Traffic-light indicator (Green: Running, Amber: Installed but not running, Red: Not installed)
- **Host / Port** — The endpoint clients use to connect. Defaults to the server's routable IP address. Published to `HKLM\Software\Adam\BrokerService` on Windows so clients auto-discover.
- **TLS** — Toggle for encrypted connections
- **Action Buttons:**
  - **Install Service** — Installs and starts the Broker service
  - **Uninstall Service** — Stops and removes the Broker service
  - **Start Service** — Starts an installed but stopped service
  - **Stop Service** — Stops a running service
  - **Save Settings** — Persists endpoint configuration to the registry for client discovery
- **Polling Interval** — How often (in seconds) the service status is auto-refreshed (1–300s, default: 30s)
- **Log Viewer** — Real-time log of Service Manager operations

### Connection Endpoint

The **Endpoint** field shows the `<host>:<port>` that the Catalog Browser should use to connect. The Service Manager automatically:

1. Resolves the server's primary IPv4 address when the host is set to `localhost` or `127.0.0.1`
2. Publishes the endpoint to `HKLM\Software\Adam\BrokerService` on Windows so the Catalog Browser auto-discovers it

### TLS Configuration

Adam supports optional **TLS encryption** for broker-client communication.

**To enable TLS:**

1. Obtain a TLS certificate (self-signed is acceptable for testing)
2. In `appsettings.json`, set:

```json
{
  "Broker": {
    "Tls": {
      "Enabled": true,
      "CertificatePath": "/path/to/certificate.pfx",
      "CertificatePassword": "your-password",
      "CertificateThumbprint": "",
      "AllowSelfSigned": true
    }
  }
}
```

3. In the Service Manager UI, enable **Use TLS** and optionally **Allow Self-Signed** before saving settings
4. Restart the Broker Service

> **Important:** For production use, obtain a certificate from a trusted Certificate Authority. Self-signed certificates are suitable for development and internal networks.

### Polling and Monitoring

The Service Manager polls the Broker service status at a configurable interval (default: 30 seconds). The polling runs on a background thread and updates the traffic-light indicator without blocking the UI.

The polling interval can be adjusted from **1 to 300 seconds** via the **Polling Interval** field. Changes take effect immediately and are persisted to disk.

---

## 5. User Management

### Roles and Permissions

Adam ships with three built-in roles. Custom roles can be created through the database.

| Role | Permissions | Description |
|------|-------------|-------------|
| **Viewer** | `asset:read`, `collection:read` | Browse assets and collections. Cannot ingest, edit metadata, or administer. |
| **Editor** | `asset:read`, `asset:create`, `asset:update`, `collection:read`, `collection:update` | Ingest assets, edit metadata, and manage collections. Cannot view audit logs or manage users. |
| **Administrator** | `asset:*`, `collection:*`, `user:*`, `role:*`, `audit:read` | Full access to all features including user management, role management, and audit logs. |

Permissions use a colon-delimited format: `resource:action`.

- Wildcard (`*`) grants all actions on a resource
- Examples: `asset:read`, `asset:create`, `user:*`

### Adding Users

1. Open the **Service Manager** and navigate to the **Users** tab
2. Click **Add User**
3. Fill in the form:
   - **Username** — Unique, minimum 2 characters
   - **Email** — Valid email address
   - **Password** — Minimum 4 characters (required for new users)
   - **Role** — Select from the available roles (Viewer, Editor, Administrator)
4. Click **Save**

The user is created and can immediately log in from the Catalog Browser.

### Editing Users

1. Select a user from the list
2. Click **Edit User**
3. Modify the fields:
   - **Email** — Update the email address
   - **Password** — Leave blank to keep the current password, or enter a new one
   - **Role** — Change the user's role
   - **Active** — Toggle to enable or disable the account
4. Click **Save**

> **Note:** When a user's role changes, connected Catalog Browser sessions detect the change within 60 seconds. The user does not need to re-login for the new permissions to take effect.

### Deactivating Users

There are two ways to deactivate a user:

1. **In the edit form:** Uncheck the **Active** checkbox and save
2. **Using the Delete button:** Marks the user as inactive (soft-delete — the user record is preserved for audit trail purposes)

When a user is deactivated:

- Their existing session is invalidated within **60 seconds** (via the periodic session check timer)
- They receive a **force logout** notification on their next client interaction
- The audit log records the deactivation event

> **Deactivation vs. Deletion:** Adam does not hard-delete users. This preserves referential integrity with the audit log and prevents data loss.

---

## 6. Database Configuration

### Supported Providers

| Provider | Configuration Value | Use Case |
|----------|-------------------|----------|
| **SQLite** | `sqlite` | Single-server, development, small teams (default) |
| **PostgreSQL** | `postgresql` or `postgres` | Production, multi-server, larger teams |
| **SQL Server** | `sqlserver` or `mssql` | Enterprise environments with existing SQL Server infrastructure |

### Migration Wizard

The Broker Service includes a `DbMigrationService` that can migrate from SQLite to PostgreSQL or SQL Server.

**Steps:**

1. Set up your target database server (PostgreSQL or SQL Server) and create an empty database
2. Stop the Broker Service
3. Update the configuration file:

```json
{
  "DbProvider": "postgresql",
  "DbConnection": "Host=db-server;Database=adam;Username=adam;Password=secure_password"
}
```

4. Start the Broker Service. The `MigrationRunner` runs automatically at startup and applies any pending schema changes.

The migration tool will:

- Create all tables matching the current schema
- Preserve provider-specific index syntax (filtered indexes, quoting)
- Report progress for each migration step

> **Important:** Stop the Broker Service before changing the database provider or connection string. Migration runs automatically on next startup.

### Manual Configuration

Configuration is stored in `appsettings.json` which is auto-generated if not present. Key settings:

```json
{
  "Jwt": {
    "SigningKey": "base64-encoded-256-bit-key",
    "TokenExpiryHours": 24
  },
  "Broker": {
    "Port": 9100,
    "Tls": {
      "Enabled": false,
      "CertificatePath": "",
      "CertificatePassword": "",
      "AllowSelfSigned": true
    }
  },
  "DbProvider": "sqlite",
  "DbConnection": "Data Source=catalog.db"
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Jwt:SigningKey` | 256-bit key for JWT signing (base64) | Auto-generated |
| `Jwt:TokenExpiryHours` | JWT token lifetime | 24 |
| `Broker:Port` | TCP listen port | 9100 |
| `Broker:Tls:Enabled` | Enable TLS encryption | false |
| `DbProvider` | Database provider: `sqlite`, `postgresql`, or `sqlserver` | `sqlite` |
| `DbConnection` | Provider-specific connection string | `Data Source=catalog.db` |

---

## 7. Audit Log

The **Audit Log** tab in Service Manager provides a filterable, chronological view of all significant system events.

### Logged Actions

The following actions are recorded in the audit log:

| Action | Entity Type | Description |
|--------|-------------|-------------|
| `Login` | `User` | Successful user authentication |
| `LoginFailed` | `User` | Failed login attempt (monitor for brute force) |
| `Logout` | `User` | User logout |
| `CreateUser` | `User` | New user created |
| `UpdateUser` | `User` | User modified (role, email, active status) |
| `DeactivateUser` | `User` | User deactivated |
| `Ingest` | `DigitalAsset` | Asset imported |
| `Update` | `DigitalAsset` | Asset metadata modified |
| `Delete` | `DigitalAsset` | Asset soft-deleted |
| `Export` | `DigitalAsset` | Asset exported |
| `CreateCollection` | `Collection` | Collection created |
| `UpdateCollection` | `Collection` | Collection modified |
| `DeleteCollection` | `Collection` | Collection deleted |

Each log entry records:
- **Timestamp** — When the action occurred
- **Username** — Who performed the action
- **Action** — What was done
- **Entity Type** — The type of entity affected
- **Entity ID** — The specific entity identifier
- **Details** — Additional context (e.g., changed fields, IP address)

### Filtering

The audit log viewer provides client-side filtering:

- **Action** — Filter by action type (e.g., `Login`, `Ingest`)
- **Entity Type** — Filter by entity type (e.g., `User`, `DigitalAsset`)
- **Date Range** — Filter by date range (From / To)

Up to **500 entries** are displayed at a time, ordered by timestamp (newest first).

### Retention

Audit logs are retained indefinitely in the database. Consider archiving or pruning old entries periodically for very active systems. The `AccessLogs` table includes a `Timestamp` index for efficient querying.

---

## 8. Connecting the Catalog Browser

Once the Broker Service is running, users can connect from the Catalog Browser:

1. Launch **Adam Catalog Browser**
2. In the connection bar (title bar), enter the server's **host** and **port**
3. Click **Connect**
4. Enter **username** and **password**
5. Click **Login**

**Auto-discovery (Windows):** If the Service Manager published settings to the registry, the Catalog Browser pre-fills the host and port automatically.

**Manual configuration:** Users can also set the host and port in `settings.json`:

| Platform | Path |
|----------|------|
| Windows | `%LOCALAPPDATA%/Adam/CatalogBrowser/settings.json` |
| Linux | `~/.local/share/Adam/CatalogBrowser/settings.json` |
| macOS | `~/Library/Application Support/Adam/CatalogBrowser/settings.json` |

---

## 9. Troubleshooting

### Service won't install

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| "Port already in use" | Another process is using the configured port | Change the port in Service Manager and retry, or stop the conflicting process |
| "Access denied" | Not running as Administrator | Relaunch Service Manager as Administrator |
| "No service installer available" | Unsupported platform | Adam supports Windows, macOS, and Linux. Check platform compatibility. |
| Service installs but won't start | Missing database or invalid configuration | Check `appsettings.json` for the correct `DbConnection` string. Verify the database server is reachable. |

### Users can't log in

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| "Invalid credentials" | Wrong username or password | Verify credentials in Service Manager Users tab. Reset password from the edit form. |
| "Account deactivated" | User was deactivated | Reactivate the user from the edit form. |
| "Session expired" | JWT token expired (default 24h) | Log in again. The token refresh is automatic on re-login. |
| Can't connect to broker | Wrong host/port or broker not running | Verify the broker endpoint in Service Manager Admin Panel. Check the broker service status. |

### Database issues

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| "Provider not found" | Missing EF Core provider package | Ensure the correct provider NuGet package is included in the publish output |
| Migration fails | Incompatible schema or permissions | Check the Broker Service startup logs for migration errors. Restart the service to trigger automatic schema migration. |
| PostgreSQL connection fails | Missing `pg_hba.conf` entry | Verify the PostgreSQL server allows connections from the broker host |
| SQL Server connection fails | TCP/IP disabled for SQL Server | Enable TCP/IP in SQL Server Configuration Manager |

### Client disconnects

The Broker Client includes automatic reconnection with exponential backoff:
- Attempt 1: 1s delay
- Attempt 2: 2s delay
- Attempt 3: 4s delay
- Attempt 4: 8s delay
- Attempt 5: 15s delay
- Attempt 6+: 30s delay
- Max attempts: 10

After 10 failed attempts, the client gives up and reports a connection error. Restart the Catalog Browser to retry.

### Debug Logs

The Broker Service writes connection-level debug logs to:

| Platform | Path |
|----------|------|
| Windows | `%TEMP%\Adam\BrokerService\` |
| Linux/macOS | `/tmp/Adam/BrokerService/` |

The latest log file path is printed to the console at startup.

---

## 10. Appendices

### A. Ports and Firewall

| Port | Protocol | Direction | Purpose |
|------|----------|-----------|---------|
| 9100 (default) | TCP | Inbound | Broker Service client connections |
| Varies | TCP | Outbound | PostgreSQL (5432 default) or SQL Server (1433 default) |

### B. File Locations

| Component | Windows | Linux | macOS |
|-----------|---------|-------|-------|
| Service config | `appsettings.json` (same dir as exe) | `appsettings.json` (same dir as exe) | `appsettings.json` (same dir as exe) |
| SQLite database | `./catalog.db` (same dir as exe) | `./catalog.db` (same dir as exe) | `./catalog.db` (same dir as exe) |
| Client settings | `%LOCALAPPDATA%/Adam/CatalogBrowser/settings.json` | `~/.local/share/Adam/CatalogBrowser/settings.json` | `~/Library/Application Support/Adam/CatalogBrowser/settings.json` |
| Registry (client endpoint) | `HKLM\Software\Adam\BrokerService` | N/A (config file) | N/A (config file) |

### C. JWT Token Format

Tokens are **256-bit HMAC-SHA256** signed JWTs with the following claims:

| Claim | Description |
|-------|-------------|
| `sub` | User ID |
| `username` | Username |
| `role` | Role name |
| `exp` | Expiration timestamp (Unix epoch seconds) |

The signing key is configured in `appsettings.json` under `Jwt:SigningKey`. For multi-server deployments, copy the same signing key to all broker instances so tokens are interchangeable.

> **Production security:** Replace the default signing key with a new 256-bit key generated via:
> ```bash
> # Generate a secure 256-bit key (base64)
> openssl rand -base64 32
> ```
> Set the output as `Jwt:SigningKey` in `appsettings.json`.

### D. Supported .NET Versions

| Component | Framework |
|-----------|-----------|
| All projects | .NET 10 |
| EF Core | 10.0-preview |
| UI framework | Avalonia 12 |

---

> **Next:** [User Guide](user-guide.md) — How to use the Catalog Browser.
