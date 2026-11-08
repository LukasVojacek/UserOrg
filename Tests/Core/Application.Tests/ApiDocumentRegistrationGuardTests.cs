using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Shared.OpenApi;

using Xunit.Sdk;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Guards the pair ADR-0106 depends on: the documents Core actually registers, and
/// <see cref="ApiDocuments.Published"/>, which is the set the gateway's route-existence oracle and Scalar read
/// for the same <c>V1Api</c> value.
/// </summary>
/// <remarks>
/// <para>
/// The gateway runs in another process and cannot inspect Core's container, so the shared set is the only
/// binding available. A document registered with a literal instead of a name from <see cref="ApiDocuments"/>
/// restores the drift ADR-0106 closed: the gateway never fetches that document, and every route described only
/// by it is answered <c>404 route.not_found</c> although it is served. A legacy document still registered with
/// <c>V1Api</c> off is the reverse: Core keeps publishing what the gateway and Scalar no longer offer.
/// </para>
/// <para>
/// It boots the real <c>Program</c> composition (no infrastructure is contacted), so it is tagged as an
/// integration test per the repository convention.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiDocumentRegistrationGuardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_RegisterExactlyThePublishedDocumentNames_When_ComposingTheHost(bool isV1ApiEnabled)
    {
        using var factory = new ProgramBootFactory(isV1ApiEnabled, isV2ApiEnabled: true);

        AssertRegistrationsMatchPublishedSet(factory, isV1ApiEnabled);
    }

    /// <summary>
    /// The teeth of the guard above: a document registered outside <see cref="ApiDocuments"/> must turn it
    /// red rather than pass unnoticed, which is exactly how the defect reached the gateway.
    /// </summary>
    [Fact]
    public void Should_FailTheGuard_When_ADocumentOutsideThePublishedSetIsRegistered()
    {
        using var factory = new ProgramBootFactory(isV1ApiEnabled: true, isV2ApiEnabled: true, services => services.AddOpenApi("v2-Sixth"));

        Assert.Throws<EqualException>(() => AssertRegistrationsMatchPublishedSet(factory, isV1ApiEnabled: true));
    }

    private static void AssertRegistrationsMatchPublishedSet(ProgramBootFactory factory, bool isV1ApiEnabled)
    {
        _ = factory.Services;

        Assert.Equal(
            ApiDocuments.Published(isV1ApiEnabled).Order(StringComparer.OrdinalIgnoreCase),
            RegisteredDocumentNames(factory.Registrations).Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The document names the container was built with. <c>AddOpenApi(documentName)</c> registers one named
    /// <see cref="IConfigureOptions{TOptions}"/> of <see cref="OpenApiOptions"/> per document and lower-cases
    /// the name it keys everything by, which is why every comparison here is case-insensitive.
    /// </summary>
    private static IReadOnlyCollection<string> RegisteredDocumentNames(IEnumerable<ServiceDescriptor> registrations) =>
        [.. registrations
            .Where(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<OpenApiOptions>))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ConfigureNamedOptions<OpenApiOptions>>()
            .Select(configure => configure.Name)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}