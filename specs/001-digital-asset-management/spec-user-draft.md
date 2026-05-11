# Adam System Specification: .NET 10 Digital Asset Management

## 1. Project Overview

**Adam** is a Digital Asset Management (DAM) system designed to provide a responsive, asynchronous user experience for managing large-scale file libraries. The system is built using **.NET 10**, leveraging **Avalonia UI** for cross-platform interaction. It supports two distinct operational states: **Multi-User (Service-Based)** and **Standalone (Local-Only)**.

---

## 2. Technical Stack

| Component | Technology |
| --- | --- |
| **Development Platform** | .NET 10

 |
| **UI Framework** | Avalonia UI (MVVM Design Pattern)

 |
| **Service Host** | Windows Service, macOS Launch Daemon, or Linux systemd Service

 |
| **Data Access Layer** | Entity Framework Core (Repository Pattern)

 |
| **IPC** | gRPC (for Multi-User mode communication)

 |
| **Local Database** | SQLite (for Standalone Mode)

 |

---

## 3. Operational Modes

### 3.1 Standalone Mode (Disconnected State)

In this mode, **Adam** functions as a high-performance, single-user desktop application without requiring administrative service installation.

* **Database:** Opens or creates a local `.db` file using the SQLite provider.


* **Direct Processing:** The UI client directly initializes the `IFileService` and indexing engine internally.


* **Portability:** Ideal for use on external hard drives or isolated workstations where network connectivity and service installation are restricted.


* **Workflow:** The user simply selects a database file on startup; no background service coordination is required.



### 3.2 Multi-User Mode (Service-Based)

This mode utilizes a central database (PostgreSQL/SQL Server) and the native background service architecture for persistent indexing and team collaboration.

* **Service Engine:** Uses the Windows Service, macOS Launch Daemon, or Linux systemd unit for 24/7 folder watching.


* **Client Communication:** The UI client connects to the local or remote service via gRPC.


* **Concurrency:** Managed through the central database to allow multiple users to tag and organize assets simultaneously.



---

## 4. Cross-Platform Background Services

For Multi-User mode, **Adam** implements native background workers for each platform:

| Platform | Service Manager | Implementation Details |
| --- | --- | --- |
| **Windows** | Service Control Manager | Compiled as a .NET Worker Service.

 |
| **macOS** | launchd | Managed via **Launch Daemons** in `/Library/LaunchDaemons/`.

 |
| **Linux** | systemd | Managed via **systemd Units** in `/etc/systemd/system/`.

 |

---

## 5. Administrative Client Features

The "Admin Panel" within the Avalonia UI adapts based on the detected OS and the active mode:

* **Mode Toggle:** Allows users to switch between "Standalone (SQLite)" and "Multi-User (Service)" configurations.


* **Service Deployment:** Programmatically registers background workers for the relevant OS when in Multi-User mode.


* **Database Wizard:** Provides tools to migrate a Standalone SQLite database to a Multi-User PostgreSQL/SQL Server instance.



---

## 6. Spec-Driven Development Roadmap (Updated)

### Phase 1: Foundation & Abstraction

* Define the `IServiceManager` and `IDataContext` interfaces.


* Develop the shared Domain Model for `Asset` and `Metadata`.



### Phase 2: Standalone Engine

* Implement the SQLite repository and the `FileService` internal runner.


* Develop the "Local-First" logic where the client can run as a self-contained process.



### Phase 3: Cross-Platform Service & IPC

* Develop the .NET 10 Worker Service project for Windows, Mac, and Linux.


* Establish gRPC communication for Multi-User mode.


* Implement platform-specific installers (ServiceControl, Plist, Systemd).



### Phase 4: Avalonia UI Development

* Build the main asset gallery and metadata editor.


* Create the "Mode Selection" screen for choosing between Standalone and Multi-User states.



### Phase 5: Deployment & Validation

* Validate SQLite performance in Standalone mode with libraries of 100,000+ assets.


* Test Multi-User write operations and service persistence on all three operating systems.