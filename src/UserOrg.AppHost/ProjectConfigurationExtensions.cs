namespace UserOrg.AppHost;

using Parameters = IDistributedApplicationBuilderExtensions.Parameters;

internal static class ProjectConfigurationExtensions
{
    internal static IResourceBuilder<ProjectResource> WithApiEnvironment(
        this IResourceBuilder<ProjectResource> api,
        Parameters p,
        EndpointReference emailSenderHttp,
        EndpointReference vaultHttp)
    {
        return api
            .WithEnvironment("APP_VERSION", "Aspire.dev")
            .WithEnvironment("MailConfiguration__SenderEmail", "userorg-noreply@aspire.cz")
            .WithEnvironment("MailConfiguration__PortalAddress", "https://portal.dev.lexis.tech")
            .WithEnvironment("ResourceRequestActionLink__BaseUrl", "https://portal.dev.lexis.tech")
            .WithEnvironment("MailConfiguration__MailSenderUrl", emailSenderHttp)
            .WithEnvironment("MailConfiguration__MailSenderApiKey", p.EmailSenderApiKey)
            .WithEnvironment("DefaultUserRoleName", "User")
            .WithEnvironment("DefaultAdminRoleName", "Admin")
            .WithEnvironment("DefaultProviderAdminRoleName", "ProviderAdmin")
            .WithEnvironment("Scalar__PublicPathBase", "")
            .WithEnvironment("GatewayInternal__Audience", p.GatewayInternalAudience)
            .WithEnvironment("Services__vault__http", vaultHttp)
            .WithEnvironment("Services__emailsender__http", emailSenderHttp)
            .WithEnvironment("DataAccessConfiguration__UserOrgDataSeedPath", "../../../Seeds/UserOrgDataSeed.json")
            .WithEnvironment("Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__0", "SshCertificate")
            .WithEnvironment("Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__1", "PrivateKey")
            .WithEnvironment("Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__2", "Kerberos")
            .WithEnvironment("FeatureManagement__ReadOnlyMode", p.FeatureReadOnlyMode)
            .WithEnvironment("FeatureManagement__V1Api", p.FeatureV1Api)
            .WithEnvironment("FeatureManagement__V2Api", p.FeatureV2Api)
            .WithEnvironment("FeatureManagement__TokenExchangeEnabled", p.FeatureTokenExchangeEnabled)
            .WithEnvironment("Database__AutoMigrateOnStartup", p.AutoMigrateOnStartup)
            .WithEnvironment("HealthChecks__MaximumAllocatedMemoryMb", "1024")
            .WithSerilogEnvironment(p)
            .WithLoopbackTrustedProxy()
            // Empty is what makes the API explorer work under `aspire run`: no servers entry in the
            // document, so ASP.NET Core writes one per request from the host header. Only a deployment
            // registers the parameter that overrides it.
            .WithEnvironment("Scalar__ServerUrl", p.ScalarServerUrl is null
                ? ReferenceExpression.Create($"")
                : ReferenceExpression.Create($"{p.ScalarServerUrl}"));
    }

    // Provider (customer) GUID in "N" (dashless) format — keys HeappeInstancesMap + Storage maps.
    private const string EfpProviderGuid = "6c88d35db8f44b6daa5ab86539524667";

    /// <summary>
    /// Wires the event-driven Waldur.Sync <c>Waldur:Instances</c> shape (ADR-0056). Instance 0 = EFP
    /// (driven by the shared enable/period parameters), instance 1 = Puhuri (D17: wired explicitly for
    /// the first time, disabled by default). The pull-based design has no STOMP transport, so no
    /// websocket env is emitted. Instance 0's BaseUrl is set from the waldurApi external service in
    /// Program.cs.
    /// </summary>
    internal static IResourceBuilder<ProjectResource> WithWaldurEnvironment(
        this IResourceBuilder<ProjectResource> api,
        Parameters p)
    {
        return api
            .WithEnvironment("Waldur__Instances__0__Name", "EFP-Waldur")
            .WithEnvironment("Waldur__Instances__0__BaseUrl", p.WaldurBaseUrl)
            .WithEnvironment("Waldur__Instances__0__Enabled", p.EnableWaldurSyncIntegration)
            .WithEnvironment("Waldur__Instances__0__SyncIntervalMinutes", p.WaldurSyncIntegrationPeriod)
            .WithEnvironment("Waldur__Instances__0__CustomersEndpoint", "/api/customers/")
            .WithEnvironment("Waldur__Instances__0__ProjectsEndpoint", "/api/projects/")
            .WithEnvironment("Waldur__Instances__0__MarketplaceResourcesEndpoint", "/api/marketplace-resources/")
            .WithEnvironment("Waldur__Instances__0__MarketplacePublicOfferingsEndpoint", "/api/marketplace-public-offerings/")
            .WithEnvironment("Waldur__Instances__0__Identity__ServiceName", "EFP-Waldur")
            .WithEnvironment("Waldur__Instances__0__Identity__ServiceAccountUserName", "EFP-Waldur_provider")
            .WithEnvironment("Waldur__Instances__0__Identity__ServiceAccountEmail", "efp.waldur@service.provider")
            .WithEnvironment("Waldur__Instances__0__Identity__WaldurPreferredAuthType", "SshCertificate")
            .WithEnvironment("Waldur__Instances__0__Identity__WaldurPIEmail", "info@efp-platform.com")
            .WithEnvironment($"Waldur__Instances__0__HeappeInstancesMap__{EfpProviderGuid}__Identifier", "develop")
            .WithEnvironment($"Waldur__Instances__0__HeappeInstancesMap__{EfpProviderGuid}__Uri", "https://heappe.it4i.cz/develop")
            .WithEnvironment("Waldur__Instances__0__Storage__EfpCoreServiceName", "EFP-Core")
            .WithEnvironment("Waldur__Instances__0__Storage__UseSharedIrodsForAllEntities", "true")
            .WithEnvironment("Waldur__Instances__0__Storage__SharedIrods__Name", "Shared iRODS EFP")
            .WithEnvironment("Waldur__Instances__0__Storage__SharedIrods__Endpoint", "http://86.50.229.55/irods-http-api")
            .WithEnvironment("Waldur__Instances__0__Storage__SharedIrods__AllocationAmount", "50")
            .WithEnvironment($"Waldur__Instances__0__Storage__IrodsInstancesMap__{EfpProviderGuid}__Name", "iRods Testing HE")
            .WithEnvironment($"Waldur__Instances__0__Storage__IrodsInstancesMap__{EfpProviderGuid}__Endpoint", "http://testinghe.irods")
            .WithEnvironment($"Waldur__Instances__0__Storage__IrodsInstancesMap__{EfpProviderGuid}__AllocationAmount", "100")
            .WithEnvironment($"Waldur__Instances__0__Storage__StagingAreaInstancesMap__{EfpProviderGuid}__Name", "Staging Area Testing HE")
            .WithEnvironment($"Waldur__Instances__0__Storage__StagingAreaInstancesMap__{EfpProviderGuid}__Endpoint", "http://stagingareahe");
    }

    internal static IResourceBuilder<ProjectResource> WithGatewayEnvironment(
        this IResourceBuilder<ProjectResource> gateway,
        Parameters p,
        EndpointReference vaultHttp)
    {
        return gateway
            .WithEnvironment("DefaultUserRoleName", "User")
            .WithEnvironment("DefaultAdminRoleName", "Admin")
            .WithEnvironment("DefaultProviderAdminRoleName", "ProviderAdmin")
            .WithEnvironment("RoleManagement__AccessToken", p.RoleManagementAccessToken)
            .WithEnvironment("RoleManagement__PathPrefix", "")
            .WithEnvironment("RoleManagement__SeedFilePath", p.GatewayRoleManagementSeedFilePath)
            .WithEnvironment("VaultCredentials__MountPoint", "userorg")
            .WithEnvironment("Database__AutoMigrateOnStartup", p.AutoMigrateOnStartup)
            .WithEnvironment("HealthChecks__MaximumAllocatedMemoryMb", "1024")
            .WithEnvironment("FeatureManagement__V1Api", p.FeatureV1Api)
            .WithEnvironment("FeatureManagement__StrictJwtAudience", p.FeatureStrictJwtAudience)
            .WithEnvironment("Services__vault__http", vaultHttp)
            .WithEnvironment("Cyclops__BaseUrl", p.CyclopsBaseUrl)
            .WithEnvironment("Cyclops__TimeUsageRelativeUrl", p.CyclopsTimeUsageRelativeUrl)
            .WithEnvironment("Cyclops__TotalUsageRelativeUrl", p.CyclopsTotalUsageRelativeUrl)
            .WithSerilogEnvironment(p)
            .WithLoopbackTrustedProxy();
    }

    private static IResourceBuilder<ProjectResource> WithLoopbackTrustedProxy(
        this IResourceBuilder<ProjectResource> resource)
    {
        if (!resource.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return resource;
        }

        return resource
            .WithEnvironment("ForwardedHeaders__KnownProxies__0", "127.0.0.1")
            .WithEnvironment("ForwardedHeaders__KnownProxies__1", "::1");
    }

    private static IResourceBuilder<ProjectResource> WithSerilogEnvironment(
        this IResourceBuilder<ProjectResource> resource,
        Parameters p)
    {
        if (!resource.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return resource;
        }

        return resource
            .WithEnvironment("SerilogStepUp__Mode", p.SerilogMode)
            .WithEnvironment("SerilogStepUp__EnableConsoleLogging", p.SerilogEnableConsoleLogging)
            .WithEnvironment("SerilogStepUp__BaseLevel", p.SerilogBaseLevel)
            .WithEnvironment("SerilogStepUp__StepUpLevel", p.SerilogStepUpLevel)
            .WithEnvironment("SerilogStepUp__CaptureRequestBody", p.SerilogCaptureRequestBody)
            .WithEnvironment("SerilogStepUp__AlwaysLogRequestSummary", p.SerilogAlwaysLogRequestSummary);
    }

    internal static IResourceBuilder<ProjectResource> WithInactiveUserSyncEnvironment(
        this IResourceBuilder<ProjectResource> api,
        Parameters p)
    {
        return api
            .WithEnvironment("InactiveUserSync__Enabled", p.EnableInactiveUserSync)
            .WithEnvironment("InactiveUserSync__SyncInterval", p.InactiveUserSyncSyncInterval)
            .WithEnvironment("InactiveUserSync__BaseUrl", p.InactiveUserSyncBaseUrl)
            .WithEnvironment("InactiveUserSync__ClientId", p.InactiveUserSyncClientId)
            .WithEnvironment("InactiveUserSync__ClientSecret", p.InactiveUserSyncClientSecret);
    }
}