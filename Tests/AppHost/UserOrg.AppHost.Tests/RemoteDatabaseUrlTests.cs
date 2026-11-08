namespace UserOrg.AppHost.Tests;

public sealed class RemoteDatabaseUrlTests
{
    [Theory]
    [InlineData("Server=db.lexis.tech,1444;Database=userorg;User Id=u;Password=p", "tcp://db.lexis.tech:1444/")]
    [InlineData("Server=db.lexis.tech", "tcp://db.lexis.tech:1433/")]
    [InlineData("Data Source=tcp:db.lexis.tech,1444", "tcp://db.lexis.tech:1444/")]
    [InlineData("Server=db.lexis.tech\\INSTANCE,1444", "tcp://db.lexis.tech:1444/")]
    [InlineData("Server=10.0.0.5", "tcp://10.0.0.5:1433/")]
    public void Should_BuildTheUrlFromTheConnectionStringHost(string connectionString, string expected) =>
        Assert.Equal(expected, RemoteDatabaseUrl.FromConnectionString(connectionString));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string")]
    [InlineData("Database=userorg")]
    [InlineData("Server=.")]
    [InlineData("Server=(local)")]
    [InlineData("Server=np:\\\\db\\pipe\\sql\\query")]
    public void Should_ReturnNoUrlWithoutATcpHost(string? connectionString) =>
        Assert.Null(RemoteDatabaseUrl.FromConnectionString(connectionString));
}
