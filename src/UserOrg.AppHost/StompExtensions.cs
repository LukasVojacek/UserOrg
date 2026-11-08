namespace UserOrg.AppHost;

internal static class StompExtensions
{
    public static IResourceBuilder<ContainerResource> AddStompServer(this IDistributedApplicationBuilder builder)
    {
        var usr = builder.AddParameter("rbmqUserName", "guest");
        var pwd = builder.AddParameter("rbmqPassword", "guest");

        var rabbitmq = builder.AddDockerfile("rabbitMqStomp", ".", "externalServices/stompServer/Dockerfile.stompServer")
                  .WithEndpoint(targetPort: 5672, name: "tcp")
                  .WithEnvironment(context =>
                  {
                      context.EnvironmentVariables["RABBITMQ_DEFAULT_USER"] = usr;
                      context.EnvironmentVariables["RABBITMQ_DEFAULT_PASS"] = pwd;
                  })
                  .WithHttpEndpoint(targetPort: 15672, name: "management")
                  .WithHttpEndpoint(targetPort: 15674, name: "ws")
                  .WithHttpEndpoint(targetPort: 15670, name: "example")
                  .WithHttpHealthCheck($"/api/health/checks/is-in-service", 200, "management")
                  .ExcludeFromManifest();

        return rabbitmq;
    }
}