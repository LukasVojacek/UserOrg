using IntegrationTests;

// The assembly fixture stands up the whole non-Aspire topology (SQL container, migrations, WireMock,
// child API + Gateway processes) exactly once and initializes to completion BEFORE any collection
// fixture, so GatewayAttachFixture reads the PARITY_* env this fixture exports and enters attach mode.
[assembly: AssemblyFixture(typeof(TestcontainersTopologyFixture))]

// The topology is a single shared process set; tests must not run in parallel against it.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
