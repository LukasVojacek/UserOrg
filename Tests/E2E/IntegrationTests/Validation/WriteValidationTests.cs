using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

using IntegrationTests.Support;

using ParityTests;

namespace IntegrationTests.Validation;

/// <summary>
/// Bad-input specs for v2 write endpoints (POST/PUT) with a request body: one case per endpoint whose
/// <c>Request</c> DTO carries non-trivial validation (required field, enum, date range, string
/// length/format). Every call uses an Allowed-tier client for its route (Owner/Manager/Admin as the route
/// demands), so a rejection can only be the endpoint's own input validation — never a 401/403, which B02's
/// auth matrix already covers in isolation.
/// </summary>
/// <remarks>
/// <para>Skipped — no request body, or a body with no shape constraints to violate:</para>
/// <list type="bullet">
/// <item>POST Projects/{projectShortName}/OnboardingRequests (CreateOnboardingRequestEndpoint) — route params only.</item>
/// <item>POST Providers/{providerName}/Reactivate (ReactivateProviderEndpoint) — no request body.</item>
/// <item>POST Public/ActionLinks/{actionLink} (HandleActionLinkEndpoint) — route param only.</item>
/// <item>POST Providers/{providerName}/Resources/{resourceName}/Publish (PublishResourceEndpoint) — no request body.</item>
/// <item>POST Projects/{projectShortName}/ResourceRequests/{slug}/ResendApproval (ResendApprovalEmailEndpoint) — route params only.</item>
/// <item>PUT Projects/{projectShortName}/OnboardingRequests/{slug}/Accept (AcceptOnboardingRequestEndpoint) — route params only.</item>
/// <item>PUT Projects/{projectShortName}/OnboardingRequests/{slug}/Reject (RejectOnboardingRequestEndpoint) — route params only.</item>
/// <item>PUT Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName} (AssignProjectResourceEndpoint) — route params only.</item>
/// <item>PUT Projects/{projectShortName}/Tags/{tagName} (AssignProjectTagEndpoint) — route params only.</item>
/// <item>PUT Projects/{projectShortName}/Users/{idpUserId} (AssignProjectUserEndpoint) — route params only.</item>
/// <item>PUT Requests/{slug}/Accept (AcceptRequestEndpoint) — route param only.</item>
/// <item>PUT Requests/{slug}/Reject (RejectRequestEndpoint) — route param + an unvalidated query string.</item>
/// <item>PUT Projects/{projectShortName}/ResourceRequests/{slug}/Cancel (CancelResourceRequestEndpoint) — route params only.</item>
/// <item>PUT Users/{idpUserId}/ProviderAdmin/{providerName} (PromoteToProviderAdminEndpoint) — route params only.</item>
/// <item>PUT Users/Me/Preferences (UpdateUserPreferencesEndpoint) — body is an opaque JSON blob with no shape constraints.</item>
/// </list>
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class WriteValidationTests(GatewayAttachFixture fixture, TestcontainersTopologyFixture topology)
{
    private const string SeededProjectShortName = "TESTPRJ";
    private const string SeededProviderName = "prov1";
    private const string SeededLocationName = "loc1";
    private const string SeededDomainTypeName = "Agricultural and Veterinary Sciences";

    // Reserved for this suite's one live-token case (see seed-data.sql); an invalid decision never resumes
    // the workflow, so the request stays parked at PendingPiApproval and this name is never reused elsewhere.
    private const string ValidationResourceName = "valdecision";

    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Should_RejectCreateProject_When_ShortNameIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PostAsJsonAsync(
            new Uri("/api/v2/Projects", UriKind.Relative),
            new
            {
                ShortName = "",
                Name = "Validation probe project",
                StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DomainTypeName = SeededDomainTypeName,
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectCreateProjectTag_When_TagNameIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/Tags", UriKind.Relative),
            new { Name = "" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectAddLocation_When_EndpointExceedsMaxLength()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = "valloc-endpoint-len",
                Endpoint = new string('a', 201),
                IsPrivate = false,
                IsShared = false,
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectAddLocation_When_LocationTypeIsUnspecified()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = "valloc-type-unspecified",
                Endpoint = "https://valloc.example.test",
                LocationType = "Unspecified",
                IsPrivate = false,
                IsShared = true,
            },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "provider.invalid_location_type", ct);
    }

    [Fact]
    public async Task Should_RejectAddLocation_When_LocationTypeIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = "valloc-type-missing",
                Endpoint = "https://valloc.example.test",
                IsPrivate = false,
                IsShared = true,
            },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "provider.invalid_location_type", ct);
    }

    [Fact]
    public async Task Should_RejectAddLocation_When_LocationTypeIsUndefinedNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = "valloc-type-undefined",
                Endpoint = "https://valloc.example.test",
                LocationType = 999,
                IsPrivate = false,
                IsShared = true,
            },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "provider.invalid_location_type", ct);
    }

    [Fact]
    public async Task Should_RejectAddLocation_When_LocationTypeIsUnknownName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = "valloc-type-unknown",
                Endpoint = "https://valloc.example.test",
                LocationType = "Foo",
                IsPrivate = false,
                IsShared = true,
            },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "validation.invalid_format", ct);
    }

    [Fact]
    public async Task Should_RejectCreateProvider_When_SupportEmailIsMalformed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri("/api/v2/Providers", UriKind.Relative),
            new
            {
                Name = "valcheckprovider",
                CountryCode = "BD",
                SupportEmail = "not-an-email",
                IsPrivate = false,
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectAssignResource_When_ResourceNameIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}/Assignments", UriKind.Relative),
            new { ResourceName = "" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectCreateRequest_When_RequestTypeIsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PostAsJsonAsync(
            new Uri("/api/v2/Requests", UriKind.Relative),
            new { ProjectShortName = SeededProjectShortName, RequestType = "NotARequestType" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectCreateResourceOnLocation_When_EndDateIsBeforeStartDate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Resources", UriKind.Relative),
            new
            {
                Name = "valresourceonloc",
                PrimaryInvestigatorEmail = "pi@test.cz",
                StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectCreateResourceRequest_When_ResourceNameIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/ResourceRequests", UriKind.Relative),
            new { ProviderName = SeededProviderName, ResourceName = "", PrimaryInvestigatorEmail = "pi@test.cz" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    // DEC-5: a provider with no support e-mail cannot route a request for a resource that does not exist yet,
    // so creation is refused up front — a 422, not the generic 400s above.
    [Fact]
    public async Task Should_RejectCreateResourceRequest_When_ProviderHasNoSupportEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        const string providerName = "valnosupportemail";
        using var createProviderResponse = await admin.PostAsJsonAsync(
            new Uri("/api/v2/Providers", UriKind.Relative),
            new { Name = providerName, CountryCode = "BD", IsPrivate = false },
            ct);
        var createProviderBody = await createProviderResponse.Content.ReadAsStringAsync(ct);
        Assert.True(
            createProviderResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201 creating provider '{providerName}', got {(int)createProviderResponse.StatusCode}.{Environment.NewLine}{createProviderBody}");

        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);
        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/ResourceRequests", UriKind.Relative),
            new { ProviderName = providerName, ResourceName = "valnosupportres", PrimaryInvestigatorEmail = "pi@test.cz" },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"Expected 422 for a provider without a support e-mail, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            "resource_request.provider_no_support_email",
            document.RootElement.GetProperty("code").GetString());
    }

    // The decision-validation branch is only reachable past a resolved token (ADR-0064 anti-leak: an
    // unresolved token 404s before the body is ever inspected), so this case creates a real pending request
    // and settles it with a malformed decision — the one live-workflow dependency in this suite.
    [Fact]
    public async Task Should_RejectHandleResourceRequestAction_When_DecisionIsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreatePendingResourceRequestAsync(owner, ValidationResourceName, ct);
        var token = await PollForActionTokenAsync(slug, ct);

        using var anonymous = fixture.CreateAnonymousGatewayClient();
        using var response = await anonymous.PostAsJsonAsync(
            new Uri($"/api/v2/Public/ResourceRequestActions/{token}", UriKind.Relative),
            new { Decision = "Explode" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectChangeProjectUserRole_When_RoleNameIsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PutAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/Users/no-such-user/Role", UriKind.Relative),
            new { RoleName = "NoSuchRole" },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "project.unknown_role", ct);
    }

    [Fact]
    public async Task Should_ResolveTheRoleByName_When_ChangeProjectUserRoleTargetsAMissingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PutAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/Users/no-such-user/Role", UriKind.Relative),
            new { RoleName = "Member" },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.NotFound, "project.user_not_found", ct);
    }

    [Fact]
    public async Task Should_RejectUpdateProject_When_DescriptionExceedsMaxLength()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.PutAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}", UriKind.Relative),
            new
            {
                Name = "TESTPRJ validation probe",
                Description = new string('a', 501),
                StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DomainTypeName = SeededDomainTypeName,
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectUpdateAssignment_When_AllocationUnitTypeIsUnknownName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}/Assignments/no-such-slug", UriKind.Relative),
            new { AllocationUnitType = "NoSuchUnit" },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "validation.invalid_format", ct);
    }

    [Fact]
    public async Task Should_RejectAssignResource_When_AllocationUnitTypeIsUnknownName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}/Assignments", UriKind.Relative),
            new { ResourceName = "no-such-resource", AllocationUnitType = "NoSuchUnit" },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.BadRequest, "validation.invalid_format", ct);
    }

    [Fact]
    public async Task Should_RejectUpdateAssignment_When_AllocationUnitTypeIsNone()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}/Assignments/no-such-slug", UriKind.Relative),
            new { AllocationUnitType = "None" },
            ct);

        await AssertProblemCodeAsync(response, HttpStatusCode.UnprocessableEntity, "provider.allocation_unit_type_not_found", ct);
    }

    [Fact]
    public async Task Should_AcceptAKnownAllocationUnitTypeName_When_UpdateAssignmentTargetsAMissingSlug()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}/Assignments/no-such-slug", UriKind.Relative),
            new { AllocationUnitType = "NodeHours" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.NotFound, ct);
    }

    [Fact]
    public async Task Should_RejectUpdateLocation_When_EndpointIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}", UriKind.Relative),
            new { Endpoint = "" },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectUpdateProvider_When_CountryCodeIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}", UriKind.Relative),
            new { CountryCode = "", IsPrivate = false },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Should_RejectUpdateResource_When_EndDateIsBeforeStartDate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Resources/no-such-resource", UriKind.Relative),
            new
            {
                PrimaryInvestigatorEmail = "pi@test.cz",
                StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            ct);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest, ct);
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected} for invalid input, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, HttpStatusCode expected, string expectedCode, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected} for invalid input, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> CreatePendingResourceRequestAsync(HttpClient owner, string resourceName, CancellationToken ct)
    {
        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/ResourceRequests", UriKind.Relative),
            new { ProviderName = SeededProviderName, ResourceName = resourceName, PrimaryInvestigatorEmail = "pi@test.cz" },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202 creating a resource request for '{resourceName}', got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("Slug").GetString()
            ?? throw new InvalidOperationException("Create response did not carry a Slug.");
    }

    // The PI-approval email is emailed off the background worker, so its arrival is polled rather than
    // awaited synchronously (mirrors ResourceRequestWriteTests.PollForActionTokenAsync).
    private async Task<string> PollForActionTokenAsync(string slug, CancellationToken ct)
    {
        var token = await PollUntilTerminal.ForAsync(
            _ => Task.FromResult(TryExtractActionToken(slug)),
            candidate => candidate is not null,
            TerminalTimeout,
            PollInterval,
            $"PI approval email for request '{slug}'",
            ct);
        return token!;
    }

    // The plaintext action-link token is only ever emailed (never persisted; the projection stores its hash),
    // so it is recovered from the EmailSender WireMock recording, same as ResourceRequestWriteTests.
    private string? TryExtractActionToken(string slug)
    {
        string? latest = null;

        foreach (var body in topology.WireMock.RecordedEmailRequestBodies())
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("TemplateName", out var template)
                || template.GetString() != "NewProjectAssignment_PM"
                || !root.TryGetProperty("TemplateArguments", out var arguments)
                || arguments.ValueKind != JsonValueKind.Array
                || arguments.GetArrayLength() < 2
                || arguments[0].GetString() != slug)
            {
                continue;
            }

            var link = arguments[1].GetString();
            if (string.IsNullOrEmpty(link))
            {
                continue;
            }

            var lastSegment = link[(link.LastIndexOf('/') + 1)..];
            latest = Uri.UnescapeDataString(lastSegment);
        }

        return latest;
    }
}
