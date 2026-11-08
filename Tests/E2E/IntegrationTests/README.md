# IntegrationTests

A non-Aspire end-to-end test suite using Testcontainers SQL Server, child-process API/Gateway, and in-process WireMock mocks.

## Running the suite

```bash
dotnet build                  # Populates artifacts/bin with API, Gateway, DatabaseMigrations
dotnet test --project Tests/E2E/IntegrationTests/IntegrationTests.csproj
```

Or via MTP directly:
```bash
artifacts\bin\IntegrationTests\{Debug|Release}\IntegrationTests.exe
```

Tests run serially (no parallelization); each collection reuses the shared Testcontainers topology initialized by `TestcontainersTopologyFixture` and attaches the reusable `GatewayAttachFixture` seeding/snapshot behavior.
