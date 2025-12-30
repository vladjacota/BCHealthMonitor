# BC Health Monitor

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

A Windows Service that provides health monitoring and task scheduler control for Microsoft Dynamics 365 Business Central on-premise installations.

## Overview

BC Health Monitor enables load balancer integration and proactive monitoring for BC on-premise environments. Since Business Central on-premise has no native `/health` endpoint, this service provides multiple strategies to detect BC availability and exposes standardized health endpoints for infrastructure integration.

## Features

- **Health Endpoints** for load balancer integration
  - `/health/client` - Web Client session health
  - `/health/webservices` - OData/SOAP/API health (with client protection)
  - `/health/scheduler` - Task scheduler availability
  - `/health` - Aggregate health
  - `/health/details` - Detailed diagnostics

- **Multi-Strategy BC Availability Checking**
  - Auto-fallback chain: HTTP → TCP → Service → PerfCounter
  - Configurable strategies for different environments
  - Automatic service discovery

- **Task Scheduler Control**
  - Automatic enable/disable based on business hours
  - Manual override with optional duration
  - REST API for remote control

- **Monitoring**
  - `/metrics` - Prometheus-format metrics
  - `/status` - Human-readable dashboard (auto-refresh)
  - Windows Event Log integration
  - File logging with rotation

## Health Status Levels

| Status | HTTP Code | Meaning |
|--------|-----------|---------|
| **Healthy** | `200` | All checks pass, resources under warning thresholds |
| **Degraded** | `200` | Resources between warning and max thresholds (proactive alert) |
| **Unhealthy** | `503` | Resources exceed max threshold, don't route traffic |
| **Unreachable** | `504` | Cannot connect to BC service |

### Three-Tier Threshold Logic

Each resource check uses Warning and Max thresholds:
- Value < Warning → **Healthy**
- Warning ≤ Value < Max → **Degraded** (still serves traffic, triggers alert)
- Value ≥ Max → **Unhealthy** (remove from load balancer pool)

## Health Check Logic

### `/health/client` (for Web Client load balancer)

Returns `503` (Unhealthy) when:
- BC service is unreachable (`504`)
- CPU usage exceeds **Max** threshold
- Memory usage exceeds **Max** threshold
- WebClient sessions exceed **Max** threshold

Returns `200` (Degraded) when:
- Any resource between **Warning** and **Max** thresholds

### `/health/webservices` (for API load balancer)

Returns `503` (Unhealthy) when:
- BC service is unreachable (`504`)
- CPU/memory usage exceeds **Max** threshold
- WebClient sessions exceed **Warning** threshold (protects POS/client capacity)
- WebService sessions exceed **Max** threshold

### `/health/scheduler` (for monitoring)

Returns `503` when:
- BC service is unreachable
- CPU/memory thresholds exceeded
- Scheduler cannot be controlled (error condition)

## Configuration

Edit `appsettings.json`:

```json
{
  "Server": {
    "Port": 5080,
    "CacheDurationSeconds": 5,
    "StartupDelaySeconds": 20
  },
  "BCInstance": {
    "Name": "BC",
    "BaseUrl": "http://localhost:7048/BC",
    "HealthEndpoint": "/ODataV4/$metadata",
    "Strategy": "Auto",
    "TcpPort": null,
    "ServiceName": "",
    "HealthCheckTimeoutSeconds": 5,
    "SqlConnectionString": "Server=localhost;Database={database};Integrated Security=true;TrustServerCertificate=true",
    "TenantDatabases": ["Tenant1", "Tenant2"],
    "Thresholds": {
      "Cpu": { "Warning": 70, "Max": 85 },
      "Memory": { "Warning": 75, "Max": 90 },
      "ClientSessions": {
        "Warning": 100,
        "Max": 200
      },
      "WebServiceSessions": {
        "Warning": 56,
        "Max": 80
      },
      "TotalSessions": {
        "Warning": 200,
        "Max": 250
      }
    },
    "Installation": {
      "Type": "Standard",
      "Version": "",
      "AdminToolPath": "",
      "GocModulePath": ""
    },
    "SchedulerControl": {
      "Enabled": true,
      "BusinessHours": {
        "Start": "08:00",
        "End": "20:00",
        "Days": ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        "Timezone": "Europe/Bucharest"
      }
    }
  },
  "Logging": {
    "EventLog": false,
    "FilePath": "C:\\Logs\\BCHealthMonitor\\",
    "ApplicationInsights": {
      "Enabled": false,
      "ConnectionString": ""
    }
  }
}
```

### Threshold Configuration

| Resource | Warning (Degraded) | Max (Unhealthy) | Notes |
|----------|-------------------|-----------------|-------|
| CPU | 70% | 85% | Early warning before saturation |
| Memory | 75% | 90% | Memory pressure indication |
| Client Sessions | 100 | 200 | Warning also blocks web service traffic |
| WebService Sessions | 56 (~70%) | 80 | Warning before blocking |
| Total Sessions | 200 (80%) | 250 | Global session pressure |

### BC Availability Check Strategies

BC on-premise has **no native `/health` endpoint**. The service supports multiple strategies to check BC availability:

| Strategy | Description | When to Use |
|----------|-------------|-------------|
| `Auto` | Fallback chain: HTTP → TCP → Service → PerfCounter | **Default.** Works in most environments |
| `Http` | HTTP endpoint check only (OData metadata) | When OData is enabled and you want full web stack validation |
| `Tcp` | TCP port connectivity only | When web services are disabled but you want fast port check |
| `Service` | Windows Service status only | When network checks are unreliable |
| `PerfCounter` | BC Performance Counter only | Proves BC is actually processing requests |
| `Combined` | HTTP + Service + PerfCounter must ALL pass | Deep health validation for critical systems |

#### Strategy Configuration

```json
"BCInstance": {
  "Strategy": "Auto",
  "TcpPort": 7046,
  "ServiceName": ""
}
```

- **Strategy**: One of `Auto`, `Http`, `Tcp`, `Service`, `PerfCounter`, `Combined`
- **TcpPort**: Required for `Tcp` strategy, optional for `Auto`. Set to `null` to skip TCP in Auto mode. Common BC ports:
  - `7046` - Client Services (RTC, Web Client)
  - `7047` - SOAP Web Services  
  - `7048` - OData Web Services
  - `7049` - Developer Services
- **ServiceName**: Windows Service name. If empty, auto-discovers via `Get-NAVServerInstance` or falls back to pattern `MicrosoftDynamicsNavServer$<Name>`

### BC Installation Types

```json
"Installation": {
  "Type": "Standard",
  "Version": "240",
  "AdminToolPath": "",
  "GocModulePath": ""
}
```

| Type | Description |
|------|-------------|
| `Standard` | Standard BC installation with NavAdminTool.ps1 |
| `LSUpdateService` | LS Update Service installation with GoCurrentServer module |

- **Version**: BC version folder name (e.g., "240", "250"). Auto-detected if empty.
- **AdminToolPath**: Custom path to NavAdminTool.ps1. Uses default if empty.
- **GocModulePath**: Path to GoCurrentServer module for LSUpdateService installations.

### Configuration Notes

- **HealthEndpoint**: Use `/ODataV4/$metadata` (always available, no auth needed) or another OData/API endpoint
- **SqlConnectionString**: Use `{database}` placeholder - it will be replaced with each tenant database name
- **Warning threshold**: When ClientSessions exceed this value, `/health/webservices` returns 503 to protect POS capacity
- **Business Hours**: Scheduler is automatically disabled during these hours
- **StartupDelaySeconds**: Wait time before health checks become active (allows BC to start)

## Session Data Sources

The service tries multiple sources in order:

1. **SQL** (fastest, full breakdown by session type)
2. **BC API** (`/api/microsoft/runtime/v1.0/sessions`)
3. **Performance Counters** (total count only, no type breakdown)

## Installation

### Build

```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

### Install as Windows Service

```powershell
# Create service
sc.exe create BCHealthMonitor `
    binPath="C:\Services\BCHealthMonitor\BCHealthMonitor.exe" `
    start=auto `
    obj="DOMAIN\BCServiceAccount"

# Set description
sc.exe description BCHealthMonitor "Business Central Health Monitor"

# Start service
sc.exe start BCHealthMonitor
```

### Firewall Rule

```powershell
New-NetFirewallRule -DisplayName "BC Health Monitor" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 5080 `
    -Action Allow
```

## API Reference

### Health Endpoints

```bash
# Aggregate health
GET http://localhost:5080/health

# Client health (for load balancer)
GET http://localhost:5080/health/client

# Web services health (for API load balancer)
GET http://localhost:5080/health/webservices

# Scheduler health
GET http://localhost:5080/health/scheduler

# Detailed diagnostics
GET http://localhost:5080/health/details
```

### Scheduler Control

```bash
# Get current state
GET http://localhost:5080/scheduler

# Enable scheduler (permanent)
POST http://localhost:5080/scheduler/enable

# Enable scheduler for 2 hours
POST http://localhost:5080/scheduler/enable?duration=2h

# Disable scheduler (permanent)
POST http://localhost:5080/scheduler/disable

# Disable scheduler for 30 minutes
POST http://localhost:5080/scheduler/disable?duration=30m

# Clear override, return to business hours logic
DELETE http://localhost:5080/scheduler/override
```

### Duration Format

- `30m` - 30 minutes
- `2h` - 2 hours
- `1d` - 1 day
- `2h30m` - 2 hours 30 minutes

### Monitoring

```bash
# Prometheus metrics
GET http://localhost:5080/metrics

# Status dashboard (HTML)
GET http://localhost:5080/status
```

## Sample Response

```json
{
  "status": "Healthy",
  "timestamp": "2024-01-15T14:30:00Z",
  "duration_ms": 45,
  "cached": false,
  "checks": {
    "bc_health": {
      "status": "Healthy",
      "latency_ms": 12,
      "message": "HTTP 200 OK",
      "source": "http"
    },
    "cpu": {
      "status": "Healthy",
      "value": 34.5,
      "warning": 70,
      "max": 85
    },
    "memory": {
      "status": "Degraded",
      "value": 78.0,
      "warning": 75,
      "max": 90
    },
    "client_sessions": {
      "status": "Healthy",
      "value": 45,
      "warning": 100,
      "max": 200,
      "source": "sql"
    }
  }
}
```

### Detailed Health Response

`/health/details` returns additional information:

```json
{
  "status": "Healthy",
  "instance_name": "BC",
  "server_name": "BCSERVER01",
  "uptime": "2.14:30:45",
  "version": "1.0.0",
  "sessions": {
    "web_client": 45,
    "web_service": 12,
    "background": 8,
    "total": 65,
    "source": "sql"
  },
  "scheduler": {
    "enabled": false,
    "is_business_hours": true,
    "override_active": false,
    "reason": "Business hours: scheduler disabled"
  },
  "system": {
    "cpu_percent": 34.5,
    "memory_percent": 62.0,
    "memory_available_mb": 8192,
    "memory_total_mb": 16384
  },
  "checks": { ... }
}
```

## Prometheus Metrics

| Metric | Description |
|--------|-------------|
| `bc_health_cpu_percent` | CPU usage percentage |
| `bc_health_memory_percent` | Memory usage percentage |
| `bc_health_sessions_total` | Total active sessions |
| `bc_health_sessions_webclient` | Web client sessions |
| `bc_health_sessions_webservice` | Web service sessions |
| `bc_health_sessions_background` | Background/Job Queue sessions |
| `bc_health_scheduler_enabled` | Scheduler state (1=enabled) |
| `bc_health_status` | Health status by endpoint (1=healthy) |

## Load Balancer Configuration

### Azure Load Balancer

```
Health probe:
  - Protocol: HTTP
  - Port: 5080
  - Path: /health/client (or /health/webservices)
  - Interval: 5 seconds
  - Unhealthy threshold: 2
```

### NGINX

```nginx
upstream bc_clients {
    server bc1.internal:443;
    server bc2.internal:443;
}

server {
    location /health {
        proxy_pass http://localhost:5080/health/client;
    }
}
```

## Troubleshooting

### Service won't start

1. Check Windows Event Log: Application → BCHealthMonitor
2. Verify service account has:
   - BC Admin rights (for scheduler control)
   - SQL read access (for session queries)
   - Log folder write access

### Health checks returning 504

1. Verify BC service is running: `Get-Service MicrosoftDynamicsNavServer$BC`
2. Check BC URL in configuration
3. Test BC health endpoint directly: `Invoke-WebRequest http://localhost:7048/BC/ODataV4/$metadata`
4. Try different strategy: Set `"Strategy": "Service"` to check Windows Service directly
5. Check logs for availability check details

### Session counts showing 0

1. Check SQL connection string
2. Verify tenant database names in `TenantDatabases` array
3. Check service account SQL permissions
4. If using empty TenantDatabases, ensure Performance Counters are accessible

### Scheduler control not working

1. Verify BC Admin rights for service account
2. Check Installation.Type matches your BC installation ("Standard" or "LSUpdateService")
3. For Standard: Verify NavAdminTool.ps1 exists at expected path
4. For LSUpdateService: Verify GoCurrentServer module is available
5. Check logs for PowerShell errors

## Alerting Integration

### Prometheus Alertmanager

```yaml
groups:
- name: bc-health
  rules:
  - alert: BCDegraded
    expr: bc_health_status{endpoint="client"} == 1 and bc_health_cpu_percent > 70
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "BC resources approaching limits"
      
  - alert: BCUnhealthy
    expr: bc_health_status{endpoint="client"} == 0
    for: 1m
    labels:
      severity: critical
    annotations:
      summary: "BC health check failing"
```

### External Monitoring

Poll health endpoints and parse JSON response:
- **Healthy/Degraded** (`status: "Healthy"` or `"Degraded"`) → Server operational
- **Unhealthy** (`status: "Unhealthy"`, HTTP 503) → Remove from pool, alert ops
- **Unreachable** (`status: "Unreachable"`, HTTP 504) → BC down, escalate

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
