# Health Checks

Health monitoring for Gateway and downstream services.

## Overview

Health Checks provide:
- Service availability monitoring
- Dependency health checking
- Readiness and liveness probes
- Health status reporting

## Endpoints

### Liveness Probe
```
GET /health/live
```

Returns `200 OK` if service is running.

### Readiness Probe
```
GET /health/ready
```

Returns `200 OK` if service is ready to accept requests (all dependencies available).

### Detailed Health
```
GET /health
```

Returns detailed health information for all components.

## Response Format

```json
{
  "status": "Healthy",
  "timestamp": "2026-05-26T10:30:00Z",
  "checks": {
    "CoreApi": {
      "status": "Healthy",
      "responseTime": "125ms"
    },
    "Database": {
      "status": "Healthy"
    },
    "Keycloak": {
      "status": "Degraded",
      "description": "Response time > 5s"
    }
  }
}
```

## Health Checks Implemented

### UserOrgApiHealthCheck
- Checks Core API connectivity
- Validates response time
- Includes readiness check

### Database Health
- Validates database connection
- Tests query execution
- Checks connection pool

### External Services
- Keycloak/OIDC availability
- Vault connectivity
- HEAppE cluster connectivity

## Configuration

### In appsettings.json

```json
{
  "HealthChecks": {
    "Enabled": true,
    "DetailedResponse": true,
    "Checks": {
      "coreapi": {
        "Enabled": true,
        "Timeout": "5000",
        "Interval": "30000"
      },
      "database": {
        "Enabled": true
      }
    }
  }
}
```

## Usage in Kubernetes/Container Orchestration

### Liveness Probe
```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 30
```

### Readiness Probe
```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 5000
  initialDelaySeconds: 5
  periodSeconds: 10
```

## Troubleshooting

### "Unhealthy" status
- Check service logs
- Verify dependencies are running
- Check network connectivity

### "Degraded" status
- Performance issue, not a failure
- Service is still running but slow
- Check load and resource usage

## Status

✅ Fully implemented and production-ready

- Multi-component health checking
- Response time monitoring
- Detailed status reporting
