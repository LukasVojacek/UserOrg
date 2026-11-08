using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Core.UnitTests")]
[assembly: InternalsVisibleTo("DatabaseMigrations")]

// Moq needs a dynamic proxy over the internal blocked-read diagnostics port
// (IBlockedReadClassifier) in BlockedReadDiagnosticsInterceptorTests.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]