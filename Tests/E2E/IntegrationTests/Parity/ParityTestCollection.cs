using E2E.Fixtures;

namespace ParityTests;

/// <summary>
/// xUnit collection for the parity suite. It shares the <see cref="GatewayAttachFixture"/>, which
/// attaches to the topology stood up once by <c>TestcontainersTopologyFixture</c> and performs the
/// seed/snapshot bootstrap against it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ParityTestCollection : ICollectionFixture<GatewayAttachFixture>
{
    public const string Name = "Gateway attach endpoints";
}
