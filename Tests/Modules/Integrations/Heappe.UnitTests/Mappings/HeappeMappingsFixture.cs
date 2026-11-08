using System;
using System.IO;
using System.Linq;
using System.Net.Http;

using WireMock.Server;

namespace Heappe.UnitTests.Mappings;

/// <summary>
/// Starts one in-process <see cref="WireMockServer"/> for the whole mapping-contract class and loads the
/// AppHost's <c>src/UserOrg.AppHost/heappe-mappings/</c> folder into it with <c>ReadStaticMappings</c> —
/// the same folder and the same call <c>Tests/E2E/IntegrationTests/WireMockTopology.cs</c> makes, so a
/// mapping proven here is the one both the Aspire <c>heappe-mock</c> resource and the E2E topology serve.
/// </summary>
public sealed class HeappeMappingsFixture : IDisposable
{
    private readonly WireMockServer _server;

    public HeappeMappingsFixture()
    {
        MappingsPath = ResolveMappingsPath();
        _server = WireMockServer.Start();
        _server.ReadStaticMappings(MappingsPath);
        Client = new HttpClient
        {
            BaseAddress = new Uri(_server.Url ?? throw new InvalidOperationException("WireMock server has no URL.")),
        };
    }

    /// <summary>Absolute path of the mappings folder under test — reported in assertion messages.</summary>
    public string MappingsPath { get; }

    /// <summary>Plain client pointed at the WireMock base URL; no HEAppE client in the way.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Priority of the mapping that answered the most recent request. Each of the twelve traffic rows gets its
    /// own mapping file at a priority below the 900 of <c>900-catchall-get.json</c>, so a row answered at 900 was
    /// swallowed by the catch-all instead of stubbed — indistinguishable from a real stub by status code alone
    /// wherever the expected body is <c>[]</c>. Reading "the most recent" is safe because xunit runs the facts of
    /// one class sequentially.
    /// </summary>
    public int PriorityOfMappingThatAnsweredLastRequest()
    {
        var entries = _server.LogEntries;
        var lastEntry = entries.Count > 0
            ? entries[^1]
            : throw new InvalidOperationException("WireMock recorded no request at all.");

        var mappingGuid = lastEntry.MappingGuid
            ?? throw new InvalidOperationException("The last request matched no mapping.");

        var mapping = _server.Mappings.FirstOrDefault(m => m.Guid == mappingGuid)
            ?? throw new InvalidOperationException($"No mapping registered under guid {mappingGuid}.");

        return mapping.Priority;
    }

    public void Dispose()
    {
        Client.Dispose();
        _server.Dispose();
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> (artifacts/bin/...) to the directory holding
    /// <c>UserOrganization.slnx</c> and returns <c>src/UserOrg.AppHost/heappe-mappings</c> under it. The test
    /// asserts on the repository's real mapping files, so no copy-to-output indirection is involved.
    /// </summary>
    private static string ResolveMappingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UserOrganization.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"Walked up from '{AppContext.BaseDirectory}' without finding a directory holding UserOrganization.slnx.");
        }

        var mappings = Path.Combine(directory.FullName, "src", "UserOrg.AppHost", "heappe-mappings");
        if (!Directory.Exists(mappings))
        {
            throw new DirectoryNotFoundException($"HEAppE mappings folder not found: {mappings}");
        }

        return mappings;
    }
}