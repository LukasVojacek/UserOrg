namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// xUnit collection for every test class that boots the real <c>Program</c> entry point. Those classes set
/// process-wide environment variables before a boot (the infrastructure keys of <c>ProgramBootEnvironment</c>,
/// config-declared Serilog sinks, OTLP endpoints), which every host built in the process reads, so no two such
/// boots may overlap. It runs alone, after every parallel collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProgramHostBootTestCollection
{
    public const string Name = "Program host boot";
}