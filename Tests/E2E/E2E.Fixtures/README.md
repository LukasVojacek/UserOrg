# E2E.Fixtures

Shared E2E test fixture library used by `IntegrationTests`.

## What It Does

`GatewayAttachFixture` attaches to an already-running topology (SQL, WireMock, child API + Gateway) that `TestcontainersTopologyFixture` stands up and exports through the `PARITY_*` environment, then seeds it with deterministic test data and per-role dev-auth. The fixture:

- Reads `PARITY_GATEWAY_URL` / `PARITY_SQL_CONNECTION` / `PARITY_HEAPPE_MOCK_URL` and waits for readiness (database schema, gateway health)
- Seeds canonical test entities (providers, locations, projects, users, requests) from `seed-data.sql`
- Bootstraps per-role users (Owner, Manager, Member) with fixed IdP IDs (ADR-0045, no Keycloak)
- Creates SQL snapshots for fast resets (Dev/Enterprise SQL Server only; graceful fallback to re-seed)
- Provides role-scoped client factories (`CreateProjectOwnerClientAsync()`, etc.)
- Owns no infrastructure: the attached topology is torn down by `TestcontainersTopologyFixture`

## Usage

```csharp
public class MyE2ETest : IClassFixture<GatewayAttachFixture>
{
    private readonly GatewayAttachFixture _fixture;

    public MyE2ETest(GatewayAttachFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Should_FetchProject_When_Authorized()
    {
        var ownerClient = await _fixture.CreateProjectOwnerClientAsync(cancellationToken);
        var response = await ownerClient.GetAsync("/api/Project/TESTPRJ", cancellationToken);
        
        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.ReadJsonAsync(cancellationToken);
        json.GetProperty("shortName").GetString().Should().Be("TESTPRJ");
    }
}
```

## Seed Constants

Use `SeedConstants` to reference canonical IDs across both test suites:

```csharp
using E2E.Fixtures;

var request = SeedConstants.SeedRequestId;
var secondProvider = SeedConstants.SecondProviderId;
```

Every regressed-list endpoint (provider, location, resource, request) seeded with ≥2 rows to verify ORDER BY parity between v1 and v2 endpoints (ADR-0055).

## Database Reset

For tests requiring a pristine baseline:

```csharp
await _fixture.ResetToSeedAsync(cancellationToken);
```

Uses a SQL snapshot if available; otherwise re-runs the idempotent seed. Reserved for global-state assertions (statistics, cache counters)—not for per-test isolation.

## `loc1.Endpoint` and the Admin Row

`loc1.Endpoint` carries the `__HEAPPE_MOCK_URL__` placeholder — the create-resource liveness probe hits `Location.Endpoint`, not the `HEAPPE_URL` `AssignedLocationValue`, so the placeholder has to live there. The seed grants the owner user (`AAAAAAAA-…`) the `Admin` system role only when the caller substitutes `__SEED_OWNER_AS_ADMIN__` with `true`; this fixture never does, so the auth matrix keeps its Owner expectations. The same `seed-data.sql` is what the AppHost's `db-seed-e2e` command runs, under `aspire run`, through the `DatabaseMigrations` tool's `seed` mode (`SeedScript.cs`).
