# LattePanda IOTA + eeCLOUD — Remote Configuration & Telemetry Demo

A small, developer-oriented demo showing a common **edge/gateway** pattern on **LattePanda IOTA**:

- A **Device Agent (.NET)** runs on the LattePanda and continuously publishes **telemetry** and **reported state** to **eeCLOUD**.
- An **Admin Dashboard (Blazor Server)** publishes **remote configuration** (per device or per group).
- The Device Agent automatically fetches and applies new config versions, then reports the **AppliedConfigVersion** back.

This repo is intended to be simple to run, easy to read, and easy to extend (e.g., time-range queries, rollbacks, groups).

---

## Architecture

**3 components**

1. **LattePanda IOTA — Device Agent (.NET Worker / Console)**
   - Registers a device identity (deviceId, group, name)
   - Reads latest *desired config* (device override → group fallback)
   - Applies config live (e.g., `samplingMs`, `logLevel`, feature flags)
   - Writes telemetry + reported state (including applied config version)

2. **eeCLOUD (DB + API / Data layer)**
   - Stores: devices, desired config versions, telemetry, reported state

3. **Admin Dashboard (Blazor Server)**
   - Lists devices and latest reported state
   - Publishes new desired config versions
   - Displays telemetry with a lightweight **SVG chart** (no external charting libs)

---

## Demo flow (what to record / show)

1. Open **Telemetry** → select device `LP-001` → press **Load** → show live chart updates.
2. Go to **Publish Config** → set target (`device` or `group`) → change `samplingMs` (e.g., 1000 → 250) → increment `version` → **Publish**.
3. Return to **Telemetry** → see updates arrive more frequently (denser points) and confirm applied version in **Device Details**.

---

## Prerequisites

- .NET 10 SDK
- An eeCLOUD account + **Application** + **API key**
- LattePanda IOTA running Windows or Linux (or any x86 machine for local testing)

---

## Repo layout

```
src/
  Shared/		Shared models + helpers
  Agent/		Runs on the device (LattePanda IOTA)
  Admin/		Blazor Server admin dashboard
```

---

## Configuration (recommended)

Do **not** commit real secrets to GitHub.

Use **User Secrets** for local development (recommended), or environment variables for production.

### Admin (Blazor Server)

From `src/Admin`:

```bash
dotnet user-secrets init
dotnet user-secrets set "eeCLOUD:ApiKey" "YOUR_API_KEY"
```

### Device Agent

From `src/Agent`:

```bash
dotnet user-secrets init
dotnet user-secrets set "eeCLOUD:ApiKey" "YOUR_API_KEY"

# Device identity (example)
dotnet user-secrets set "Device:DeviceId" "LP-001"
dotnet user-secrets set "Device:Group" "lab"
dotnet user-secrets set "Device:Name" "LattePanda IOTA - Demo"
```

> If you prefer `appsettings.json`, keep `ApiKey` empty in committed files and use `appsettings.Development.json` locally (gitignored).

---

## Run the demo

### 1) Run the Admin Dashboard

```bash
cd src/Admin
dotnet run
```

Open the URL printed in the console (typically `https://localhost:xxxx`).

### 2) Run the Device Agent

```bash
cd src/Agent
dotnet run
```

You should start seeing telemetry and reported state in the Admin UI.

---

## Data model (memories / collections)

The demo uses a simple data model, typically mapped to eeCLOUD “memories/collections”:

- **devices**: `deviceId`, `group`, `name`, `metadata`, `createdAtUtc`
- **desiredConfig**: `targetType` (`device|group`), `targetId`, `version`, `config`, `publishedAtUtc`
- **reportedState**: `deviceId`, `lastSeenUtc`, `appliedConfigVersion`, `runtime`
- **telemetry**: `deviceId`, `timestampUtc`, `metrics`

---

## Telemetry chart (SVG)

Telemetry is displayed using a minimal **SVG line chart** (no JS, no chart libraries).
Hover points to see tooltips (timestamp + value).

---

## Notes & tips

- **Versioning**: Always increment `version` when publishing new desired configs.
- **Device override vs group**: The agent can check device-specific config first, then fall back to the group.
- **Latency metric**: You can measure write latency with `Stopwatch` and publish it (e.g., `netDelay` / `writeMs`) as telemetry.

---

## Roadmap ideas (future improvements)

- Time range queries: `from/to` timestamp filters
- Downsampling / aggregation for long ranges
- Rollback to previous config versions
- Audit log of config changes
- Device groups & tags
- Auth hardening / key rotation guidance

---

## License

MIT

---

## Contact

Nextsys / eeCLOUD  
Demo author: Giovanni Petruzzellis
