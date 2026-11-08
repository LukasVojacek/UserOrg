using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Core.UnitTests")]
// Allows Moq/Castle to proxy internal Core ports (e.g. INotificationSender, IActionLinkGenerator) in tests.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]