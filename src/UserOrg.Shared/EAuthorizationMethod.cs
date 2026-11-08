namespace UserOrg.Shared;

public enum EAuthorizationMethod
{
    JwtBearerToken,
    ApiKey
}

public static class EAuthorizationMethodExtensions
{
    extension(EAuthorizationMethod)
    {
        /// <summary>
        /// Gets the claim type name used to indicate the authorization method.
        /// </summary>
        /// <value>
        /// The claim type string. Consumers should use this claim name when reading or writing
        /// the authorization method in a claims collection (value: "am").
        /// </value>
        public static string ClaimType => "am";

        public static EAuthorizationMethod FromString(string method) => method.ToUpperInvariant() switch
        {
            "JWTBEARERTOKEN" => EAuthorizationMethod.JwtBearerToken,
            "APIKEY" => EAuthorizationMethod.ApiKey,
            _ => throw new ArgumentException($"Unknown authorization method: {method}", nameof(method))
        };
    }
}