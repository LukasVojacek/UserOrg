using System.Runtime.CompilerServices;

// Allow Moq's DynamicProxy to create proxies for internal types used by tests
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("UserOrg.Gateway.Tests")]