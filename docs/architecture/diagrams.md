# Architecture diagrams (Mermaid)

This file contains simple mermaid diagrams illustrating the main components and flows described by the ADRs. You can render these diagrams using mermaid.live or include them in MkDocs / GitHub by enabling mermaid rendering.

## Component diagram

```mermaid
flowchart TD
  Client["Client (browser / service)"]
  Gateway["Gateway (YARP)"]
  Keycloak["Keycloak (IdP)"]
  VaultProxy["Vault Proxy"]
  Vault["HashiCorp Vault"]
  API["UserOrg API (ASP.NET Core)"]
  ForgeAPI["Forge API / Forge.Core DB"]
  SQL["SQL Server (UserOrgDB)"]
  ImmuDB["immudb (audit)"]
  Email["EmailSender"]

  Client -->|HTTPS Authorization| Gateway
  Gateway -->|Authn/Authz| Keycloak
  Gateway -->|internal token| API
  Gateway -->|S2S token| ForgeAPI
  API -->|DB access| SQL
  API -->|secrets HTTP| VaultProxy
  VaultProxy --> Vault
  API -->|audit| ImmuDB
  API -->|email calls| Email
```

## Gateway authorization sequence

```mermaid
sequenceDiagram
  participant C as Client
  participant G as Gateway
  participant EP as EndpointPermissionService
  participant UR as UserOrgRolesService
  participant PM as PermissionMatcher
  participant TS as InternalTokenService
  participant B as Backend (UserOrg API)

  C->>G: Request (Authorization: Bearer token)
  G->>EP: GetEndpointPermissionsByPath(path, method)
  EP-->>G: PermissionTemplates
  alt endpoint requires permissions
    G->>UR: GetUserPermissionKeys(userId)
    UR-->>G: UserPermissions
    G->>PM: HasRequiredPermission(templates, permissions)
    PM-->>G: allowed/denied
    alt allowed
      G->>TS: GenerateInternalToken(...)
      TS-->>G: Internal JWT
      G->>B: Forward request (Authorization: Bearer Internal JWT)
    else denied
      G-->>C: 403 Forbidden
    end
  else public endpoint
    G->>TS: (may skip) Forward original or no token
    G->>B: Forward request
  end
```

## Vault / secrets flow

```mermaid
sequenceDiagram
  participant API
  participant VP as VaultProxy
  participant V as Vault
  participant Seed as VaultSeed

  Seed->>V: initialize secrets (dev)
  API->>VP: GET /v1/secret/... (Vault API)
  VP->>V: request-secret
  V-->>VP: secret-data
  VP-->>API: secret-data
```
