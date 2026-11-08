namespace UserOrganizationManagement.Infrastructure.DataAccess.Configurations;

/// <summary>
/// DatabaseAccess Options
/// </summary>
public sealed class DataAccessOptions
{
    #region Properties
    /// <summary>
    /// Path for Logging data seed JSON file
    /// </summary>
    public string UserOrgDataSeedPath { get; set; } = "../Seeds/UserOrgDataSeed.json";
    #endregion
}