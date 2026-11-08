using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Domain.Authorization;
using UserOrganizationManagement.Domain.Configuration;
using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.Application.Authorization;

public sealed class EndpointAccessService
{
    private readonly List<EndpointInformation> _endpointInformation = [];
    private readonly ILogger<EndpointAccessService> _logger;

    public EndpointAccessService(IOptions<EndpointAccessConfiguration> config, ILogger<EndpointAccessService> logger)
    {
        _logger = logger;
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(config.Value);
        PopulateEndpointInformation(config.Value);
    }

    private void PopulateEndpointInformation(EndpointAccessConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration.EndpointInformation);
        _endpointInformation.AddRange(configuration.EndpointInformation);
        _logger.EndpointInformationPopulated(_endpointInformation);
    }

    public IReadOnlyList<string> GetPermissionFor(string httpMethod, string endpointPath)
    {
        EndpointInformation info = _endpointInformation.Find(x => x.Method == httpMethod && Regex.IsMatch(endpointPath, x.PathRegex));
        return info == default
            ? throw new EndpointPermissionNotFoundException($"""Endpoint "[{httpMethod}]{endpointPath}" was not configured. """)
            : (IReadOnlyList<string>)info.PermissionTypes.ToList().AsReadOnly();
    }

    public PermissionFor PermissionForEndpoint(string endpointPath)
    {
        IEnumerable<EndpointInformation> info = _endpointInformation.Where(x => Regex.IsMatch(endpointPath, x.PathRegex));
        return !info.Any()
            ? throw new EndpointPermissionNotFoundException($"""Endpoint "{endpointPath}" was not configured. """)
            : new PermissionFor(info);
    }
}